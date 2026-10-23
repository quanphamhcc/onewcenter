using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Bible;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Scripture;

/// <summary>
/// Panel for browsing and looking up Bible scripture by reference, and for
/// presenting it immediately (Quick Scripture, dot2-T13). Uses IBibleProvider
/// for verse retrieval and the pure Core QuickScriptureBuilder (over the
/// single ScriptureSlideGenerator splitting implementation) to produce
/// presentation slides.
/// </summary>
public partial class ScriptureBrowserPanel : UserControl
{
    private IBibleProvider? _bibleProvider;

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

    /// <summary>
    /// Reload the translation drop-down from the Bible database (dot2-T12), so
    /// a translation imported via Settings → Bibles appears here without
    /// restarting the app. The current selection is preserved when it still
    /// exists; otherwise the database default (or the first translation) is
    /// selected. No-op when no provider is wired, the database returns no
    /// translations, or the read fails — the existing items are kept then.
    /// </summary>
    public async Task RefreshTranslationsAsync()
    {
        if (_bibleProvider == null) return;

        List<BibleTranslation> translations;
        try
        {
            translations = await _bibleProvider.GetTranslationsAsync();
        }
        catch
        {
            return; // Keep the current items if the database cannot be read.
        }

        if (translations.Count == 0) return;

        var current = GetComboBoxText(TranslationComboBox);

        TranslationComboBox.Items.Clear();
        ComboBoxItem? selected = null;
        ComboBoxItem? fallback = null;
        foreach (var t in translations)
        {
            var item = new ComboBoxItem { Content = t.Abbreviation };
            TranslationComboBox.Items.Add(item);
            if (item.Content?.ToString() == current)
                selected = item;
            if (fallback == null && t.IsDefault)
                fallback = item;
        }

        TranslationComboBox.SelectedItem =
            selected ?? fallback ?? TranslationComboBox.Items[0];
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

            // dot2-T13: the pure QuickScriptureBuilder decides success vs the
            // no-data state, so a translation without verses for this passage
            // gets its own message (R7 edge case) and the live output is left
            // untouched — ShowError only changes panel state.
            var result = QuickScriptureBuilder.Build(parsedRef, verses, BuildOptions());
            if (!result.IsSuccess)
            {
                ShowError($"No verse data for '{reference}' in {translation}. " +
                          "This translation does not have this passage yet.");
                return;
            }

            _currentReference = parsedRef;
            _currentVerses = verses;

            // In-panel preview text (unchanged) so the operator still sees the passage.
            var displayText = string.Join(" ", verses.Select(v => $"{v.Verse} {v.Text}"));
            ResultsText.Text = displayText;
            ResultsText.Visibility = Visibility.Visible;
            EmptyStateText.Visibility = Visibility.Collapsed;
            ErrorText.Visibility = Visibility.Collapsed;

            // Produce slides via the Core builder (the single splitter),
            // honoring the E5 display-option toggles.
            RegenerateSlides();

            PresentButton.IsEnabled = _currentSlides is { Count: > 0 };
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

        var result = QuickScriptureBuilder.Build(_currentReference, _currentVerses, BuildOptions());
        _currentSlides = result.IsSuccess ? new List<Slide>(result.Slides) : null;
    }

    /// <summary>
    /// Slide options reflecting the current E5 display-option toggles.
    /// </summary>
    private ScriptureSlideGenerator.SlideGenerationOptions BuildOptions()
    {
        return new ScriptureSlideGenerator.SlideGenerationOptions
        {
            ShowReference = ShowReferenceCheckBox.IsChecked == true,
            ShowTranslationName = ShowTranslationCheckBox.IsChecked == true
            // LinesPerSlide / MaxCharsPerLine / ShowVerseNumbers keep generator defaults.
        };
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
        PresentButton.IsEnabled = _currentSlides is { Count: > 0 };
    }

    /// <summary>
    /// Quick Scripture (dot2-T13 / R7.3): present the current lookup's slides
    /// on the live output immediately via the host's PresentSlidesRequested
    /// wiring — a temporary overlay; no service item is created and the
    /// service order/file is untouched. (Before T13 this button was labelled
    /// "Add to Service" although it never added anything — the label now
    /// matches the behaviour. Creating a Scripture service item remains the
    /// Service Builder's AddServiceItemDialog path.)
    /// </summary>
    private void Present_Click(object sender, RoutedEventArgs e)
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
        PresentButton.IsEnabled = false;
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
