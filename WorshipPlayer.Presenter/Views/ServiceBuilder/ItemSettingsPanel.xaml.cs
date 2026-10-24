using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Views.ServiceBuilder;

/// <summary>
/// Panel showing per-item settings: auto-advance "Go To" target, the "After" trigger
/// condition + delay, transition fade time, and attached audio (đợt 2 T17 / R9).
/// Edits the Core <see cref="ServiceItemSettings"/> for the selected
/// <see cref="ServiceItem"/> and raises <see cref="SettingsSaved"/> with the
/// resulting Core settings (roadmap C4); the audio list + loop flag are exposed
/// via <see cref="EditedAudioFiles"/> / <see cref="EditedLoopAudio"/> (applied by
/// the host only when <see cref="AudioEdited"/> is true, so untouched items keep
/// their legacy <c>AudioAttachmentPath</c> untouched).
/// </summary>
public partial class ItemSettingsPanel : UserControl
{
    /// <summary>Raised on Save with the edited Core settings for the current item.</summary>
    public event EventHandler<ServiceItemSettings>? SettingsSaved;

    /// <summary>Audio list as edited in the panel, captured at Save.</summary>
    public IReadOnlyList<string> EditedAudioFiles { get; private set; } = new List<string>();

    /// <summary>Loop flag as edited in the panel, captured at Save.</summary>
    public bool EditedLoopAudio { get; private set; }

    /// <summary>True when the user touched any audio control this session.</summary>
    public bool AudioEdited { get; private set; }

    private List<string> _audioFiles = new();

    public ItemSettingsPanel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Load the given service item's auto-advance settings into the panel. A null
    /// <see cref="ServiceItem.AutoAdvanceSettings"/> is treated as defaults, so the
    /// panel shows the "Manual / Next Item" baseline without mutating the item.
    /// <paramref name="allItems"/> populates the "Specific Item" target picker
    /// (the current item is excluded so an item can't target itself).
    /// </summary>
    public void LoadSettings(ServiceItem item, IReadOnlyList<ServiceItem> allItems)
    {
        ItemTitleText.Text = $"{item.Type}: {item.Title}";
        EmptyStateText.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;

        // Đợt 2 T3: lyrics/scripture items can never auto-advance — show a note and
        // disable the auto-advance controls (guard ở Core là bắt buộc, UI là hỗ trợ).
        // Transition Fade Time stays editable: it is not auto-advance.
        bool eligible = WorshipPlayer.Core.Services.AutoAdvanceResolver.IsEligible(item);
        AutoAdvanceNotAvailableNote.Visibility = eligible ? Visibility.Collapsed : Visibility.Visible;
        GoToComboBox.IsEnabled = eligible;
        AfterConditionComboBox.IsEnabled = eligible;
        GoToTargetComboBox.IsEnabled = eligible;
        DelayTextBox.IsEnabled = eligible;

        var settings = item.AutoAdvanceSettings ?? new ServiceItemSettings();

        // Target picker: all other items, by id. Done before preselecting GoTo so the
        // saved target can be reselected.
        var targets = new List<ServiceItem>();
        foreach (var candidate in allItems)
        {
            if (!ReferenceEquals(candidate, item) && candidate.Id != item.Id)
                targets.Add(candidate);
        }
        GoToTargetComboBox.ItemsSource = targets;

        SelectComboBoxItem(GoToComboBox, ItemSettingsMapping.GoToLabel(settings.AutoAdvanceGoTo));
        SelectComboBoxItem(AfterConditionComboBox, ItemSettingsMapping.AfterLabel(settings.AutoAdvanceAfterCondition));

        if (settings.AutoAdvanceGoTo == AutoAdvanceGoTo.SpecificItem && !string.IsNullOrEmpty(settings.GoToItemId))
        {
            var selected = targets.Find(t => t.Id == settings.GoToItemId);
            if (selected != null)
                GoToTargetComboBox.SelectedItem = selected;
        }

        DelayTextBox.Text = settings.AutoAdvanceDelayMs.ToString(CultureInfo.InvariantCulture);
        FadeTimeSlider.Value = settings.TransitionFadeTimeMs;

        // Đợt 2 T17 (R9): load the item's attached audio (legacy single path is
        // surfaced as a one-element list by Resolve, not written back).
        _audioFiles = ServiceItemAudio.Resolve(item);
        LoopAudioCheckBox.IsChecked = item.Loop;
        AudioEdited = false;
        RefreshAudioList();

        UpdateConditionalVisibility();
    }

    public void ClearItem()
    {
        ItemTitleText.Text = string.Empty;
        EmptyStateText.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Collapsed;
        GoToTargetComboBox.ItemsSource = null;
        _audioFiles = new List<string>();
        AudioFilesListBox.ItemsSource = null;
        AudioEdited = false;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var goTo = ItemSettingsMapping.ToGoTo(GetComboBoxText(GoToComboBox));
        var after = ItemSettingsMapping.ToAfter(GetComboBoxText(AfterConditionComboBox));

        var settings = new ServiceItemSettings
        {
            AutoAdvanceGoTo = goTo,
            AutoAdvanceAfterCondition = after,
            AutoAdvanceDelayMs = ParseDelay(DelayTextBox.Text),
            TransitionFadeTimeMs = (int)FadeTimeSlider.Value,
            GoToItemId = goTo == AutoAdvanceGoTo.SpecificItem && GoToTargetComboBox.SelectedItem is ServiceItem target
                ? target.Id
                : null
        };

        // Đợt 2 T17 (R9): capture the edited audio list + loop flag for the host.
        EditedAudioFiles = new List<string>(_audioFiles);
        EditedLoopAudio = LoopAudioCheckBox.IsChecked == true;

        SettingsSaved?.Invoke(this, settings);
    }

    // ─── Đợt 2 T17 (R9): attached audio editing ─────────────────────────

    private void RefreshAudioList()
    {
        var display = new List<string>(_audioFiles.Count);
        foreach (var path in _audioFiles)
            display.Add(Path.GetFileName(path));
        AudioFilesListBox.ItemsSource = display;
    }

    private void AddAudio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Add audio files",
            Multiselect = true,
            Filter = "Audio files (*.mp3;*.wav;*.m4a;*.wma;*.ogg;*.flac)|*.mp3;*.wav;*.m4a;*.wma;*.ogg;*.flac|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog() != true) return;

        foreach (var file in dialog.FileNames)
        {
            if (!_audioFiles.Contains(file))
                _audioFiles.Add(file);
        }
        AudioEdited = true;
        RefreshAudioList();
    }

    private void RemoveAudio_Click(object sender, RoutedEventArgs e)
    {
        int index = AudioFilesListBox.SelectedIndex;
        if (index < 0 || index >= _audioFiles.Count) return;
        _audioFiles.RemoveAt(index);
        AudioEdited = true;
        RefreshAudioList();
    }

    private void MoveAudioUp_Click(object sender, RoutedEventArgs e)
    {
        int index = AudioFilesListBox.SelectedIndex;
        if (index <= 0 || index >= _audioFiles.Count) return;
        (_audioFiles[index - 1], _audioFiles[index]) = (_audioFiles[index], _audioFiles[index - 1]);
        AudioEdited = true;
        RefreshAudioList();
        AudioFilesListBox.SelectedIndex = index - 1;
    }

    private void MoveAudioDown_Click(object sender, RoutedEventArgs e)
    {
        int index = AudioFilesListBox.SelectedIndex;
        if (index < 0 || index >= _audioFiles.Count - 1) return;
        (_audioFiles[index + 1], _audioFiles[index]) = (_audioFiles[index], _audioFiles[index + 1]);
        AudioEdited = true;
        RefreshAudioList();
        AudioFilesListBox.SelectedIndex = index + 1;
    }

    private void LoopAudioCheckBox_Click(object sender, RoutedEventArgs e)
    {
        // Click fires on user interaction only (not on programmatic IsChecked sets
        // in LoadSettings), so this marks a genuine edit.
        AudioEdited = true;
    }

    private void FadeTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FadeTimeText != null)
            FadeTimeText.Text = $"{(int)e.NewValue} ms";
    }

    private void GoToComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateConditionalVisibility();
    }

    private void AfterConditionComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateConditionalVisibility();
    }

    private void UpdateConditionalVisibility()
    {
        // Guard: SelectionChanged can fire during InitializeComponent before the
        // conditional panels exist.
        if (GoToTargetPanel == null || DelayPanel == null) return;

        GoToTargetPanel.Visibility =
            GetComboBoxText(GoToComboBox) == "Specific Item" ? Visibility.Visible : Visibility.Collapsed;

        DelayPanel.Visibility =
            GetComboBoxText(AfterConditionComboBox) == "Time Delay" ? Visibility.Visible : Visibility.Collapsed;
    }

    private static int ParseDelay(string text)
    {
        return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) && ms >= 0
            ? ms
            : 5000;
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
/// Bridges the panel's human-readable combo strings to the Core auto-advance enums.
/// Kept next to the panel because it is only used by the panel and its host.
/// </summary>
internal static class ItemSettingsMapping
{
    public static AutoAdvanceGoTo ToGoTo(string label) => label switch
    {
        "Specific Item" => AutoAdvanceGoTo.SpecificItem,
        "Loop" => AutoAdvanceGoTo.Loop,
        _ => AutoAdvanceGoTo.NextItem
    };

    // Delegate to the Core parser so per-item (C4) and bulk edit (C3) agree on the mapping.
    public static AutoAdvanceAfterCondition ToAfter(string label) =>
        WorshipPlayer.Core.Services.BulkEditService.ParseAfterCondition(label);

    public static string GoToLabel(AutoAdvanceGoTo goTo) => goTo switch
    {
        AutoAdvanceGoTo.SpecificItem => "Specific Item",
        AutoAdvanceGoTo.Loop => "Loop",
        // NextSlide (model default) is not surfaced; show it as the closest panel option.
        _ => "Next Item"
    };

    public static string AfterLabel(AutoAdvanceAfterCondition after) => after switch
    {
        AutoAdvanceAfterCondition.Timer => "Time Delay",
        AutoAdvanceAfterCondition.AudioEnd => "End of Media",
        AutoAdvanceAfterCondition.VideoEnd => "End of Media",
        _ => "Manual"
    };
}
