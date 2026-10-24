using System.Collections.Generic;
using System.Windows;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Presenter.Views.ServiceBuilder;

/// <summary>
/// Thin modal host for <see cref="ItemSettingsPanel"/>, mirroring how other editor
/// UserControls are shown modally (e.g. ChordProImportDialog). Exposes the edited
/// <see cref="ServiceItemSettings"/> as <see cref="Result"/> once the user saves.
/// </summary>
public partial class ItemSettingsWindow : Window
{
    /// <summary>The settings produced by the panel's Save, available after a true DialogResult.</summary>
    public ServiceItemSettings? Result { get; private set; }

    /// <summary>True when the user edited any audio control (đợt 2 T17 / R9).</summary>
    public bool AudioEdited => SettingsPanelControl.AudioEdited;

    /// <summary>Edited audio file list, valid when <see cref="AudioEdited"/>.</summary>
    public IReadOnlyList<string> AudioFilesEdited => SettingsPanelControl.EditedAudioFiles;

    /// <summary>Edited loop flag, valid when <see cref="AudioEdited"/>.</summary>
    public bool LoopAudioEdited => SettingsPanelControl.EditedLoopAudio;

    public ItemSettingsWindow(ServiceItem item, IReadOnlyList<ServiceItem> allItems)
    {
        InitializeComponent();

        SettingsPanelControl.LoadSettings(item, allItems);
        SettingsPanelControl.SettingsSaved += OnSettingsSaved;
    }

    private void OnSettingsSaved(object? sender, ServiceItemSettings settings)
    {
        Result = settings;
        DialogResult = true;
        Close();
    }
}
