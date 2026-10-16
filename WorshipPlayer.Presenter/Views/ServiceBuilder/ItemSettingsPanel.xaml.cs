using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Presenter.Views.ServiceBuilder;

/// <summary>
/// Panel showing per-item settings: auto-advance "Go To" target, the "After" trigger
/// condition + delay, and transition fade time. Edits the Core
/// <see cref="ServiceItemSettings"/> for the selected <see cref="ServiceItem"/> and
/// raises <see cref="SettingsSaved"/> with the resulting Core settings (roadmap C4).
/// </summary>
public partial class ItemSettingsPanel : UserControl
{
    /// <summary>Raised on Save with the edited Core settings for the current item.</summary>
    public event EventHandler<ServiceItemSettings>? SettingsSaved;

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

        UpdateConditionalVisibility();
    }

    public void ClearItem()
    {
        ItemTitleText.Text = string.Empty;
        EmptyStateText.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Collapsed;
        GoToTargetComboBox.ItemsSource = null;
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

        SettingsSaved?.Invoke(this, settings);
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
