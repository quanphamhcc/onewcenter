using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Presenter.Views.SongLibrary;

/// <summary>
/// WYSIWYG-lite editor for a song's slides. A "slide" maps to a <see cref="Section"/>
/// (slides are generated from sections at display time by LyricSlideGenerator; the only
/// persisted lyric unit is <see cref="Section.Text"/>). The user picks a section, edits its
/// lyric text, and optionally sets a per-section background image and template. Every other
/// <see cref="Section"/> field (loop points, bars, color, vocal pickup, lighting) is left
/// untouched because the editor mutates the section objects passed in, never bare new ones.
/// </summary>
public partial class SongSlideEditor : UserControl
{
    /// <summary>Sentinel value used by a picker's "None/Default" entry.</summary>
    private const string NoneValue = "\u0000__none__";

    private List<Section> _sections = new();
    private bool _suppressWriteBack;

    public event EventHandler<SlideEditResult>? SaveRequested;
    public event EventHandler? CancelRequested;

    public SongSlideEditor()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Loads the song's sections into the editor along with the available background
    /// image paths and templates. The passed <paramref name="sections"/> list is edited
    /// in place; on Save it is handed back verbatim via <see cref="SlideEditResult"/>.
    /// </summary>
    /// <param name="sections">The song's sections (edited in place).</param>
    /// <param name="imagePaths">Available background image file paths.</param>
    /// <param name="templates">Available templates (id + display name).</param>
    public void LoadSong(
        List<Section> sections,
        IEnumerable<string> imagePaths,
        IEnumerable<(string Id, string Name)> templates)
    {
        _sections = sections ?? new List<Section>();

        // Background picker: "None" plus one entry per image path (value = full path).
        var bgItems = new List<PickerItem> { new PickerItem("(None)", NoneValue) };
        bgItems.AddRange((imagePaths ?? Enumerable.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new PickerItem(System.IO.Path.GetFileName(p), p)));
        BackgroundComboBox.ItemsSource = bgItems;

        // Template picker: "Default" plus one entry per template (value = template id).
        var tplItems = new List<PickerItem> { new PickerItem("(Default)", NoneValue) };
        tplItems.AddRange((templates ?? Enumerable.Empty<(string, string)>())
            .Where(t => !string.IsNullOrWhiteSpace(t.Id))
            .Select(t => new PickerItem(string.IsNullOrWhiteSpace(t.Name) ? t.Id : t.Name, t.Id)));
        TemplateComboBox.ItemsSource = tplItems;

        SectionListBox.ItemsSource = _sections;
        if (_sections.Count > 0)
            SectionListBox.SelectedIndex = 0;
        else
            UpdatePreview();
    }

    private Section? SelectedSection => SectionListBox.SelectedItem as Section;

    private void SectionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var section = SelectedSection;
        bool hasSelection = section != null;

        _suppressWriteBack = true;
        LyricsTextBox.IsEnabled = hasSelection;
        BackgroundComboBox.IsEnabled = hasSelection;
        TemplateComboBox.IsEnabled = hasSelection;

        if (section != null)
        {
            LyricsTextBox.Text = section.Text;
            SelectPickerValue(BackgroundComboBox, section.BackgroundImage);
            SelectPickerValue(TemplateComboBox, section.TemplateId);
        }
        else
        {
            LyricsTextBox.Text = string.Empty;
            BackgroundComboBox.SelectedIndex = -1;
            TemplateComboBox.SelectedIndex = -1;
        }
        _suppressWriteBack = false;

        UpdatePreview();
    }

    private void Lyrics_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressWriteBack && SelectedSection is Section section)
            section.Text = LyricsTextBox.Text;
        UpdatePreview();
    }

    private void Background_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressWriteBack || SelectedSection is not Section section) return;
        section.BackgroundImage = GetPickerValue(BackgroundComboBox);
        UpdatePreview();
    }

    private void Template_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressWriteBack || SelectedSection is not Section section) return;
        section.TemplateId = GetPickerValue(TemplateComboBox);
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewText == null) return;

        var section = SelectedSection;
        if (section == null)
        {
            PreviewText.Text = _sections.Count == 0
                ? "This song has no slides"
                : "Select a slide to edit";
            return;
        }

        PreviewText.Text = string.IsNullOrWhiteSpace(LyricsTextBox.Text)
            ? "(empty slide)"
            : LyricsTextBox.Text;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        SaveRequested?.Invoke(this, new SlideEditResult { Sections = _sections });
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Returns the selected picker value, or null for the None/Default sentinel.</summary>
    private static string? GetPickerValue(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is PickerItem item && item.Value != NoneValue)
            return item.Value;
        return null;
    }

    /// <summary>Selects the item whose value matches, falling back to the None/Default sentinel.</summary>
    private static void SelectPickerValue(ComboBox comboBox, string? value)
    {
        if (comboBox.ItemsSource is not IEnumerable<PickerItem> items) return;

        var target = string.IsNullOrEmpty(value) ? NoneValue : value;
        var match = items.FirstOrDefault(i => i.Value == target)
                    ?? items.FirstOrDefault(i => i.Value == NoneValue);
        comboBox.SelectedItem = match;
    }

    /// <summary>A combo-box entry: a human-readable label plus its stored value.</summary>
    private sealed class PickerItem
    {
        public PickerItem(string display, string value)
        {
            Display = display;
            Value = value;
        }

        public string Display { get; }
        public string Value { get; }
    }
}

/// <summary>
/// Result of editing a song's slides: the edited section list (same instances passed to
/// <see cref="SongSlideEditor.LoadSong"/>, so all non-lyric fields are preserved).
/// </summary>
public class SlideEditResult
{
    public List<Section> Sections { get; set; } = new();
}
