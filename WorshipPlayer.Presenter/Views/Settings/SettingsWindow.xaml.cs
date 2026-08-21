using System.Windows;
using System.Windows.Controls;

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
                "Defaults" => _defaultsSection ??= new DefaultsSection(),
                "Footer" => _footerSection ??= new FooterSection(),
                "Interface" => _interfaceSection ??= new InterfaceSection(),
                "Bibles" => _biblesSection ??= new BiblesSection(),
                "Screen Config" => GetScreenConfigSection(),
                "Stage Display" => _stageDisplaySection ??= new StageDisplaySection(),
                "Library" => _librarySection ??= new LibrarySection(),
                "MIDI" => _midiSection ??= new MidiSection(),
                "Updates" => _updatesCacheSection ??= new UpdatesCacheSection(),
                _ => _defaultsSection ??= new DefaultsSection()
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
