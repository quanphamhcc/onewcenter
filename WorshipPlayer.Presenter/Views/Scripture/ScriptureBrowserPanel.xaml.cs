using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Bible;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Scripture;

/// <summary>
/// Panel for browsing and looking up Bible scripture by reference.
/// Uses IBibleProvider for verse retrieval and the Core ScriptureSlideGenerator
/// (the single splitting implementation) to produce presentation slides.
/// </summary>
public partial class ScriptureBrowserPanel : UserControl
{
    private IBibleProvider? _bibleProvider;

    // E3: the ONE splitter. The panel no longer flattens/splits verses itself — it
    // delegates all slide creation to the Core generator.
    private readonly ScriptureSlideGenerator _generator = new();

    // Last parsed reference + verses, retained so toggling a display option can
    // regenerate slides without re-querying the Bible provider.
    private ScriptureReference? _currentReference;
    private List<BibleVerse>? _currentVerses;

    // The slides produced by the generator for the current lookup (what gets presented).
    private List<Slide>? _currentSlides;

    public IBibleProvider? BibleProvider
    {
        get => _bibleProvider;
        set => _bibleProvider = value;
    }

    /// <summary>
    /// Presenter settings used to seed/persist the E5 display-option defaults
    /// (ScriptureShowReference / ScriptureShowTranslationName). Optional: when null,
    /// the panel falls back to session-only checkbox state (both default to true).
    /// </summary>
    public PresenterSettings? Settings { get; set; }

    /// <summary>
    /// Fired when the operator asks to present the generated scripture slides.
    /// Carries the slides produced by the Core generator for the current lookup.
    /// </summary>
    public event EventHandler<List<Slide>>? PresentSlidesRequested;

    public ScriptureBrowserPanel()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Seed the toggle defaults from persisted settings (E5 / AC3). Guard against
        // the Checked/Unchecked handlers running during seed by assigning directly;
        // IsChecked assignment raises Options_Changed, but with no lookup result yet
        // that handler is a no-op for slide regeneration and simply re-persists the
        // same values, which is harmless.
        if (Settings != null)
        {
            ShowReferenceCheckBox.IsChecked = Settings.ScriptureShowReference;
            ShowTranslationCheckBox.IsChecked = Settings.ScriptureShowTranslationName;
        }
    }

    private void LookUp_Click(object sender, RoutedEventArgs e)
    {
        PerformLookup();
    }

    private void ReferenceTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PerformLookup();
        }
    }

    private async void PerformLookup()
    {
        var reference = ReferenceTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(reference))
        {
            ShowError("Please enter a scripture reference.");
            return;
        }

        if (_bibleProvider == null)
        {
            ShowError("Bible provider is not available.");
            return;
        }

        var translation = GetComboBoxText(TranslationComboBox);

        try
        {
            var parsedRef = await _bibleProvider.ParseReferenceAsync(reference, translation);
            if (parsedRef == null)
            {
                ShowError($"Could not parse reference '{reference}'.");
                return;
            }

            var verses = await _bibleProvider.GetVersesAsync(parsedRef);
            if (verses != null && verses.Count > 0)
            {
                _currentReference = parsedRef;
                _currentVerses = verses;

                // In-panel preview text (unchanged) so the operator still sees the passage.
                var displayText = string.Join(" ", verses.Select(v => $"{v.Verse} {v.Text}"));
                ResultsText.Text = displayText;
                ResultsText.Visibility = Visibility.Visible;
                EmptyStateText.Visibility = Visibility.Collapsed;
                ErrorText.Visibility = Visibility.Collapsed;

                // E3: produce slides via the Core generator (the single splitter),
                // honoring the E5 display-option toggles.
                RegenerateSlides();

                AddToServiceButton.IsEnabled = _currentSlides is { Count: > 0 };
            }
            else
            {
                ShowError($"No verses found for '{reference}'.");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Error looking up reference: {ex.Message}");
        }
    }

    /// <summary>
    /// Rebuild <see cref="_currentSlides"/> from the retained verses using the Core
    /// generator with options reflecting the current E5 toggles. No-op when no lookup
    /// has resolved yet.
    /// </summary>
    private void RegenerateSlides()
    {
        if (_currentReference == null || _currentVerses == null)
        {
            _currentSlides = null;
            return;
        }

        var options = new ScriptureSlideGenerator.SlideGenerationOptions
        {
            ShowReference = ShowReferenceCheckBox.IsChecked == true,
            ShowTranslationName = ShowTranslationCheckBox.IsChecked == true
            // LinesPerSlide / MaxCharsPerLine / ShowVerseNumbers keep generator defaults.
        };

        _currentSlides = _generator.GenerateSlides(_currentVerses, _currentReference, options);
    }

    /// <summary>
    /// E5 toggle handler: persist the new defaults (when settings are available) and,
    /// if a lookup result exists, regenerate the slides so toggling changes generated
    /// slide content immediately.
    /// </summary>
    private void Options_Changed(object sender, RoutedEventArgs e)
    {
        // IsLoaded guards against the InitializeComponent-time Checked events firing
        // before the panel (and Settings) are ready.
        if (!IsLoaded) return;

        if (Settings != null)
        {
            Settings.ScriptureShowReference = ShowReferenceCheckBox.IsChecked == true;
            Settings.ScriptureShowTranslationName = ShowTranslationCheckBox.IsChecked == true;
        }

        RegenerateSlides();
        AddToServiceButton.IsEnabled = _currentSlides is { Count: > 0 };
    }

    private void AddToService_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSlides is { Count: > 0 })
        {
            PresentSlidesRequested?.Invoke(this, _currentSlides);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        ResultsText.Visibility = Visibility.Collapsed;
        EmptyStateText.Visibility = Visibility.Collapsed;
        AddToServiceButton.IsEnabled = false;
        _currentReference = null;
        _currentVerses = null;
        _currentSlides = null;
    }

    private static string GetComboBoxText(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
            return item.Content?.ToString() ?? string.Empty;
        return string.Empty;
    }
}
