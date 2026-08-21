using System.Collections.ObjectModel;
using System.Windows;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.UI.Views;

public partial class LightingWindow : Window
{
    private readonly Song _song;
    private readonly LightingService _lightingService;
    private readonly AppSettings _settings;
    private readonly ObservableCollection<LightingPresetViewModel> _presets = new();

    public LightingWindow(Song song, LightingService lightingService, AppSettings settings)
    {
        InitializeComponent();

        _song = song;
        _lightingService = lightingService;
        _settings = settings;

        // Populate connection settings
        OscEnabledCheck.IsChecked = settings.OscEnabled;
        OscHostBox.Text = settings.OscHost;
        OscPortBox.Text = settings.OscPort.ToString();
        MidiEnabledCheck.IsChecked = settings.MidiEnabled;

        // Populate MIDI channels
        for (int i = 1; i <= 16; i++)
            MidiChannelCombo.Items.Add(i);
        MidiChannelCombo.SelectedIndex = settings.MidiChannel - 1;

        // Build preset list from sections
        BuildPresetList();

        PresetsGrid.ItemsSource = _presets;
    }

    private void BuildPresetList()
    {
        _presets.Clear();

        foreach (var section in _song.Sections)
        {
            LightingPreset? preset = null;
            if (section.LightingPresetIndex >= 0 && section.LightingPresetIndex < _song.LightingPresets.Count)
                preset = _song.LightingPresets[section.LightingPresetIndex];

            _presets.Add(new LightingPresetViewModel
            {
                SectionName = section.Name,
                PaletteIndex = preset?.PaletteIndex ?? GetDefaultPalette(section.Name),
                IntensityPercent = (int)((preset?.Intensity ?? GetDefaultIntensity(section.Name)) * 100),
                ExcitementPercent = (int)((preset?.Excitement ?? GetDefaultExcitement(section.Name)) * 100),
                SnapshotName = preset?.SnapshotName ?? "",
                MidiNote = preset?.MidiNote ?? 60,
                MidiVelocity = preset?.MidiVelocity ?? 127
            });
        }
    }

    private int GetDefaultPalette(string sectionName)
    {
        string lower = sectionName.ToLower();
        if (lower.Contains("intro") || lower.Contains("outro")) return 0;
        if (lower.Contains("verse")) return 1;
        if (lower.Contains("pre") || lower.Contains("build")) return 2;
        if (lower.Contains("chorus")) return 3;
        if (lower.Contains("bridge")) return 4;
        return 1;
    }

    private float GetDefaultIntensity(string sectionName)
    {
        string lower = sectionName.ToLower();
        if (lower.Contains("intro")) return 0.3f;
        if (lower.Contains("verse")) return 0.4f;
        if (lower.Contains("pre") || lower.Contains("build")) return 0.6f;
        if (lower.Contains("chorus")) return 0.9f;
        if (lower.Contains("bridge")) return 0.5f;
        if (lower.Contains("outro")) return 0.2f;
        return 0.5f;
    }

    private float GetDefaultExcitement(string sectionName)
    {
        string lower = sectionName.ToLower();
        if (lower.Contains("intro")) return 0.2f;
        if (lower.Contains("verse")) return 0.3f;
        if (lower.Contains("pre") || lower.Contains("build")) return 0.5f;
        if (lower.Contains("chorus")) return 0.8f;
        if (lower.Contains("bridge")) return 0.3f;
        if (lower.Contains("outro")) return 0.1f;
        return 0.4f;
    }

    private void TestOsc_Click(object sender, RoutedEventArgs e)
    {
        string host = OscHostBox.Text;
        if (int.TryParse(OscPortBox.Text, out int port))
        {
            bool success = _lightingService.OscController.Connect(host, port);
            if (success)
            {
                // Send a test flash
                _lightingService.OscController.SetIntensity(1.0f);
                Task.Delay(500).ContinueWith(_ =>
                {
                    _lightingService.OscController.SetIntensity(0.3f);
                });
                MessageBox.Show("OSC connected! Sent a test flash.", "Success",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("Failed to connect. Check the IP and port.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void Blackout_Click(object sender, RoutedEventArgs e) => _lightingService.ForceBlackout();
    private void Flash_Click(object sender, RoutedEventArgs e) => _lightingService.ForceFlash();
    private void FullBright_Click(object sender, RoutedEventArgs e)
    {
        if (_lightingService.OscController.IsConnected)
            _lightingService.OscController.SetIntensity(1.0f);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Save settings
        _settings.OscEnabled = OscEnabledCheck.IsChecked ?? false;
        _settings.OscHost = OscHostBox.Text;
        if (int.TryParse(OscPortBox.Text, out int port))
            _settings.OscPort = port;
        _settings.MidiEnabled = MidiEnabledCheck.IsChecked ?? false;
        _settings.MidiChannel = MidiChannelCombo.SelectedIndex + 1;

        // Save presets to song
        _song.LightingPresets.Clear();
        for (int i = 0; i < _presets.Count && i < _song.Sections.Count; i++)
        {
            var vm = _presets[i];
            var preset = new LightingPreset
            {
                Name = vm.SectionName,
                PaletteIndex = vm.PaletteIndex,
                Intensity = vm.IntensityPercent / 100f,
                Excitement = vm.ExcitementPercent / 100f,
                SnapshotName = vm.SnapshotName,
                MidiNote = vm.MidiNote,
                MidiVelocity = vm.MidiVelocity
            };
            _song.LightingPresets.Add(preset);
            _song.Sections[i].LightingPresetIndex = i;
        }

        MessageBox.Show("Lighting settings saved!", "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

public class LightingPresetViewModel
{
    public string SectionName { get; set; } = "";
    public int PaletteIndex { get; set; }
    public int IntensityPercent { get; set; }
    public int ExcitementPercent { get; set; }
    public string SnapshotName { get; set; } = "";
    public int MidiNote { get; set; }
    public int MidiVelocity { get; set; }
}
