using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.SongLibrary;

/// <summary>
/// Editor for song metadata: title, artist, key, BPM, CCLI number, and tags.
/// </summary>
public partial class SongMetadataEditor : UserControl
{
    private ISongLibraryService? _songLibraryService;

    public ISongLibraryService? SongLibraryService
    {
        get => _songLibraryService;
        set => _songLibraryService = value;
    }

    public event EventHandler<SongMetadata>? SaveRequested;
    public event EventHandler? CancelRequested;

    public SongMetadataEditor()
    {
        InitializeComponent();
    }

    public void LoadMetadata(string title, string artist, string key, int? bpm, string? ccli, string tags)
    {
        TitleTextBox.Text = title;
        ArtistTextBox.Text = artist;
        SelectComboBoxItem(KeyComboBox, key);
        BpmTextBox.Text = bpm?.ToString() ?? string.Empty;
        CcliTextBox.Text = ccli ?? string.Empty;
        TagsTextBox.Text = tags;
    }

    public void Clear()
    {
        TitleTextBox.Text = string.Empty;
        ArtistTextBox.Text = string.Empty;
        KeyComboBox.SelectedIndex = -1;
        BpmTextBox.Text = string.Empty;
        CcliTextBox.Text = string.Empty;
        TagsTextBox.Text = string.Empty;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        int.TryParse(BpmTextBox.Text, out var bpm);
        var metadata = new SongMetadata
        {
            Title = TitleTextBox.Text.Trim(),
            Artist = ArtistTextBox.Text.Trim(),
            Key = GetComboBoxText(KeyComboBox),
            Bpm = bpm > 0 ? bpm : null,
            CcliNumber = string.IsNullOrWhiteSpace(CcliTextBox.Text) ? null : CcliTextBox.Text.Trim(),
            Tags = TagsTextBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList()
        };
        SaveRequested?.Invoke(this, metadata);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    private void BpmTextBox_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        // Only allow numeric input
        e.Handled = !int.TryParse(e.Text, out _);
    }

    private static string GetComboBoxText(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
            return item.Content?.ToString() ?? string.Empty;
        return string.Empty;
    }

    private static void SelectComboBoxItem(ComboBox comboBox, string value)
    {
        foreach (ComboBoxItem item in comboBox.Items)
        {
            if (item.Content?.ToString() == value)
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
    }
}

/// <summary>
/// Song metadata data.
/// </summary>
public class SongMetadata
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public int? Bpm { get; set; }
    public string? CcliNumber { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? OrganizationId { get; set; }
}
