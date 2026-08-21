using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace WorshipPlayer.Presenter.Views.Scripture;

/// <summary>
/// Preview panel showing how scripture text will be split across slides.
/// Configurable lines per slide, reference display, and translation display.
/// </summary>
public partial class ScriptureSlidePreview : UserControl
{
    private string _scriptureText = string.Empty;
    private string _reference = string.Empty;
    private string _translation = string.Empty;

    public ObservableCollection<ScriptureSlideItem> Slides { get; } = new();

    public ScriptureSlidePreview()
    {
        InitializeComponent();
        SlidesPreviewControl.ItemsSource = Slides;
    }

    public void LoadScripture(string text, string reference, string translation)
    {
        _scriptureText = text;
        _reference = reference;
        _translation = translation;
        GenerateSlides();
    }

    public void Clear()
    {
        _scriptureText = string.Empty;
        _reference = string.Empty;
        _translation = string.Empty;
        Slides.Clear();
        EmptyStateText.Visibility = Visibility.Visible;
        SlidesPreviewControl.Visibility = Visibility.Collapsed;
    }

    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_scriptureText))
        {
            GenerateSlides();
        }
    }

    private void Options_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_scriptureText))
        {
            GenerateSlides();
        }
    }

    private void GenerateSlides()
    {
        if (string.IsNullOrWhiteSpace(_scriptureText))
            return;

        Slides.Clear();

        int linesPerSlide = GetLinesPerSlide();
        bool showReference = ShowReferenceCheckBox.IsChecked == true;
        bool showTranslation = ShowTranslationCheckBox.IsChecked == true;

        // Split text into words, then group into lines
        var words = _scriptureText.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var currentLine = new List<string>();
        int wordsPerLine = 8; // Approximate words per line

        foreach (var word in words)
        {
            currentLine.Add(word);
            if (currentLine.Count >= wordsPerLine)
            {
                lines.Add(string.Join(" ", currentLine));
                currentLine.Clear();
            }
        }
        if (currentLine.Count > 0)
            lines.Add(string.Join(" ", currentLine));

        // Group lines into slides
        int slideNum = 1;
        for (int i = 0; i < lines.Count; i += linesPerSlide)
        {
            var slideLines = lines.Skip(i).Take(linesPerSlide);
            var content = string.Join("\n", slideLines);

            var footer = string.Empty;
            if (showReference || showTranslation)
            {
                var parts = new List<string>();
                if (showReference) parts.Add(_reference);
                if (showTranslation) parts.Add(_translation);
                footer = string.Join(" - ", parts);
            }

            Slides.Add(new ScriptureSlideItem
            {
                SlideNumber = $"Slide {slideNum}",
                Content = content,
                FooterText = footer
            });
            slideNum++;
        }

        EmptyStateText.Visibility = Visibility.Collapsed;
        SlidesPreviewControl.Visibility = Visibility.Visible;
    }

    private int GetLinesPerSlide()
    {
        if (LinesPerSlideComboBox.SelectedItem is ComboBoxItem item &&
            int.TryParse(item.Content?.ToString(), out var lines))
        {
            return Math.Clamp(lines, 2, 8);
        }
        return 4;
    }
}

/// <summary>
/// Display model for a single scripture slide in the preview.
/// </summary>
public class ScriptureSlideItem
{
    public string SlideNumber { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public string FooterText { get; set; } = string.Empty;
}
