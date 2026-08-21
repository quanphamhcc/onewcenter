using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Presenter.Services;

namespace WorshipPlayer.Presenter.Views;

/// <summary>
/// Display settings dialog — allows the user to select which monitor
/// is used for the audience (congregation) output and the stage display.
/// Selections are persisted in presenter-settings.json.
/// </summary>
public partial class DisplaySettingsWindow : Window
{
    private readonly PresenterSettings _settings;
    private readonly DisplayManager _displayManager;
    private readonly Action<PresenterSettings> _onSave;

    public DisplaySettingsWindow(PresenterSettings settings, DisplayManager displayManager, Action<PresenterSettings> onSave)
    {
        InitializeComponent();
        _settings = settings;
        _displayManager = displayManager;
        _onSave = onSave;

        PopulateMonitorDropdowns();
    }

    private void PopulateMonitorDropdowns()
    {
        AudienceDisplayCombo.Items.Clear();
        StageDisplayCombo.Items.Clear();

        // Add "None / Disabled" option
        AudienceDisplayCombo.Items.Add("(None - preview only)");
        StageDisplayCombo.Items.Add("(Disabled - use web stage display)");

        var monitors = _displayManager.AvailableDisplays;
        string monitorList = "";

        for (int i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            string label = $"Monitor {i + 1}: {m.Name} ({m.Width}x{m.Height}){(m.IsPrimary ? " [Primary]" : "")}";
            AudienceDisplayCombo.Items.Add(label);
            StageDisplayCombo.Items.Add(label);
            monitorList += $"{label}\n";
        }

        MonitorListText.Text = monitors.Count > 0 ? monitorList.TrimEnd() : "No monitors detected.";

        // Set current selections (+1 because index 0 is "None/Disabled")
        int audienceIndex = _settings.OutputDisplay + 1;
        int stageIndex = _settings.StageDisplay + 1;

        AudienceDisplayCombo.SelectedIndex = (audienceIndex >= 0 && audienceIndex < AudienceDisplayCombo.Items.Count)
            ? audienceIndex : 0;
        StageDisplayCombo.SelectedIndex = (stageIndex >= 0 && stageIndex < StageDisplayCombo.Items.Count)
            ? stageIndex : 0;
    }

    private void AudienceDisplayCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        // Index 0 = "None" → -1, Index 1 = monitor 0, etc.
        _settings.OutputDisplay = AudienceDisplayCombo.SelectedIndex - 1;
    }

    private void StageDisplayCombo_Changed(object sender, SelectionChangedEventArgs e)
    {
        _settings.StageDisplay = StageDisplayCombo.SelectedIndex - 1;
    }

    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        // Re-detect monitors (caller should re-populate DisplayManager first)
        PopulateMonitorDropdowns();
    }

    private void SaveClose_Click(object sender, RoutedEventArgs e)
    {
        _onSave(_settings);
        Close();
    }
}
