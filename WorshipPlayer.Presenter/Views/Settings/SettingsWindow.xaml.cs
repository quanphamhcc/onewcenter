using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Settings;

/// <summary>
/// Settings window with left sidebar navigation and swappable content sections.
/// </summary>
public partial class SettingsWindow : Window
{
    // Lazy-load sections to catch errors individually without crashing the whole window
    private DefaultsSection? _defaultsSection;
    private FooterSection? _footerSection;
    private InterfaceSection? _interfaceSection;
    private BiblesSection? _biblesSection;
    private ScreenConfigSection? _screenConfigSection;
    private StageDisplaySection? _stageDisplaySection;
    private LibrarySection? _librarySection;
    private MidiSection? _midiSection;
    private SlideLabelsSection? _slideLabelsSection;
    private UpdatesCacheSection? _updatesCacheSection;

    /// <summary>
    /// Callback to save settings externally (called when user clicks Save).
    /// </summary>
    public Action? OnSaveRequested { get; set; }

    /// <summary>
    /// The PresenterSettings to read/write display configuration.
    /// Set this before showing the window so ScreenConfig can update it.
    /// </summary>
    public WorshipPlayer.Core.Models.PresenterSettings? PresenterSettings { get; set; }

    /// <summary>
    /// The shared Bible provider, forwarded to the Bibles section so its
    /// translation list and XML import run against the live Bible database
    /// (dot2-T12). Set this before showing the window.
    /// </summary>
    public IBibleProvider? BibleProvider { get; set; }

    public SettingsWindow()
    {
        InitializeComponent();
        Loaded += SettingsWindow_Loaded;
    }

    private void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Set initial content to Defaults
        ContentArea.Content = GetSection("Defaults");
    }

    private void NavListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Guard: this can fire during InitializeComponent before ContentArea is assigned
        if (ContentArea == null) return;

        if (NavListBox.SelectedItem is ListBoxItem item)
        {
            var sectionName = item.Content?.ToString() ?? "Defaults";
            ContentArea.Content = GetSection(sectionName);
        }
    }

    private object? GetSection(string sectionName)
    {
        try
        {
            return sectionName switch
            {
                "Defaults" => GetDefaultsSection(),
                "Footer" => GetFooterSection(),
                "Interface" => _interfaceSection ??= new InterfaceSection(),
                "Bibles" => GetBiblesSection(),
                "Screen Config" => GetScreenConfigSection(),
                "Stage Display" => _stageDisplaySection ??= new StageDisplaySection(),
                "Library" => _librarySection ??= new LibrarySection(),
                "MIDI" => _midiSection ??= new MidiSection(),
                "Slide Labels" => GetSlideLabelsSection(),
                "Updates" => _updatesCacheSection ??= new UpdatesCacheSection(),
                _ => GetDefaultsSection()
            };
        }
        catch (Exception ex)
        {
            // If a section fails to load, show an error message instead of crashing
            return new TextBlock
            {
                Text = $"Failed to load '{sectionName}' section:\n{ex.Message}",
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Colors.OrangeRed),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(20)
            };
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // Read values from ScreenConfigSection if it's been loaded
            if (_screenConfigSection != null && PresenterSettings != null)
            {
                PresenterSettings.OutputDisplay = _screenConfigSection.AudienceDisplayIndex;
                PresenterSettings.StageDisplay = _screenConfigSection.StageDisplayIndex;
            }

            // Persist the CCLI license number from the Footer section if it's been loaded.
            if (_footerSection != null && PresenterSettings != null)
            {
                PresenterSettings.CcliLicenseNumber = _footerSection.CcliLicenseNumber;
            }

            // Persist the auto-timed-slides defaults (lines-per-slide + lead) from the Defaults
            // section if it's been loaded.
            if (_defaultsSection != null && PresenterSettings != null)
            {
                PresenterSettings.LinesPerSlide = _defaultsSection.LinesPerSlide;
                PresenterSettings.SlideLeadMs = _defaultsSection.SlideLeadMs;
            }

            // Persist the slide-label catalog from the Slide Labels section if it's been
            // loaded (đợt 2 T8 / R5.2). A duplicated hotkey BLOCKS the save: report the
            // exact hotkey and abort before anything is persisted.
            if (_slideLabelsSection != null && PresenterSettings != null)
            {
                // Commit any in-progress grid edit so the check sees the latest text.
                var dup = _slideLabelsSection.DuplicateHotkey;
                if (dup != null)
                {
                    MessageBox.Show(
                        $"Two slide labels share the hotkey \"{dup}\". " +
                        "Give each label its own hotkey (or clear one) before saving.",
                        "Duplicate Slide Label Hotkey",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                PresenterSettings.SlideLabels = _slideLabelsSection.GetLabels();
            }

            OnSaveRequested?.Invoke();
            MessageBox.Show("Settings saved successfully.", "Settings",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Failed to save settings: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private DefaultsSection GetDefaultsSection()
    {
        if (_defaultsSection == null)
        {
            _defaultsSection = new DefaultsSection();
            if (PresenterSettings != null)
            {
                _defaultsSection.LinesPerSlide = PresenterSettings.LinesPerSlide;
                _defaultsSection.SlideLeadMs = PresenterSettings.SlideLeadMs;
            }
        }
        return _defaultsSection;
    }

    private FooterSection GetFooterSection()
    {
        if (_footerSection == null)
        {
            _footerSection = new FooterSection();
            if (PresenterSettings != null)
            {
                _footerSection.SetCcliLicenseNumber(PresenterSettings.CcliLicenseNumber);
            }
        }
        return _footerSection;
    }

    private SlideLabelsSection GetSlideLabelsSection()
    {
        if (_slideLabelsSection == null)
        {
            _slideLabelsSection = new SlideLabelsSection();
            if (PresenterSettings != null)
            {
                // Seed the editor from the live catalog; a settings file that predates
                // slide labels (null) shows the seeded defaults (T7 EnsureLabels semantics)
                // without mutating the settings until the user actually saves.
                var labels = PresenterSettings.SlideLabels
                    ?? WorshipPlayer.Core.Services.SlideLabelService.CreateDefaults();
                _slideLabelsSection.SetLabels(labels);
            }
        }
        return _slideLabelsSection;
    }

    private BiblesSection GetBiblesSection()
    {
        if (_biblesSection == null)
        {
            _biblesSection = new BiblesSection();
            if (BibleProvider != null)
            {
                _biblesSection.BibleProvider = BibleProvider;
            }
        }
        return _biblesSection;
    }

    private ScreenConfigSection GetScreenConfigSection()
    {
        if (_screenConfigSection == null)
        {
            _screenConfigSection = new ScreenConfigSection();
            if (PresenterSettings != null)
            {
                _screenConfigSection.SetCurrentConfig(
                    PresenterSettings.OutputDisplay,
                    PresenterSettings.StageDisplay);
            }
        }
        return _screenConfigSection;
    }
}
