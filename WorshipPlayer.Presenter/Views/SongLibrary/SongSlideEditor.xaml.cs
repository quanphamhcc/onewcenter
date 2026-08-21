using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.SongLibrary;

/// <summary>
/// Editor for individual song slides - lyrics, background, and template selection.
/// </summary>
public partial class SongSlideEditor : UserControl
{
    private ITemplateService? _templateService;

    public ITemplateService? TemplateService
    {
        get => _templateService;
        set => _templateService = value;
    }

    public event EventHandler<SlideEditResult>? SaveRequested;
    public event EventHandler? CancelRequested;

    public SongSlideEditor()
    {
        InitializeComponent();
    }

    public void LoadSlide(string lyrics, string background, string template)
    {
        LyricsTextBox.Text = lyrics;
        SelectComboBoxItem(BackgroundComboBox, background);
        SelectComboBoxItem(TemplateComboBox, template);
        UpdatePreview();
    }

    private void Lyrics_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (PreviewText != null)
        {
            var text = LyricsTextBox.Text;
            PreviewText.Text = string.IsNullOrWhiteSpace(text)
                ? "Enter lyrics to see preview"
                : text;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var result = new SlideEditResult
        {
            Lyrics = LyricsTextBox.Text,
            Background = GetComboBoxText(BackgroundComboBox),
            Template = GetComboBoxText(TemplateComboBox)
        };
        SaveRequested?.Invoke(this, result);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        CancelRequested?.Invoke(this, EventArgs.Empty);
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
/// Result of editing a song slide.
/// </summary>
public class SlideEditResult
{
    public string Lyrics { get; set; } = string.Empty;
    public string Background { get; set; } = "Default";
    public string Template { get; set; } = "Default";
}
