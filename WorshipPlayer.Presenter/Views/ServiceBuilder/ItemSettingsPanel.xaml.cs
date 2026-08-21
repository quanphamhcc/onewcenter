using System.Windows;
using System.Windows.Controls;

namespace WorshipPlayer.Presenter.Views.ServiceBuilder;

/// <summary>
/// Panel showing per-item settings: auto-advance, transition fade time.
/// </summary>
public partial class ItemSettingsPanel : UserControl
{
    public event EventHandler<ItemSettings>? SettingsSaved;

    public ItemSettingsPanel()
    {
        InitializeComponent();
    }

    public void LoadItem(string itemTitle, string itemType, string goTo, string afterCondition, int fadeTimeMs)
    {
        ItemTitleText.Text = $"{itemType}: {itemTitle}";
        EmptyStateText.Visibility = Visibility.Collapsed;
        SettingsPanel.Visibility = Visibility.Visible;

        SelectComboBoxItem(GoToComboBox, goTo);
        SelectComboBoxItem(AfterConditionComboBox, afterCondition);
        FadeTimeSlider.Value = fadeTimeMs;
    }

    public void ClearItem()
    {
        ItemTitleText.Text = string.Empty;
        EmptyStateText.Visibility = Visibility.Visible;
        SettingsPanel.Visibility = Visibility.Collapsed;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var settings = new ItemSettings
        {
            GoTo = GetComboBoxText(GoToComboBox),
            AfterCondition = GetComboBoxText(AfterConditionComboBox),
            TransitionFadeTimeMs = (int)FadeTimeSlider.Value
        };
        SettingsSaved?.Invoke(this, settings);
    }

    private void FadeTimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FadeTimeText != null)
            FadeTimeText.Text = $"{(int)e.NewValue} ms";
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
/// Per-item settings data.
/// </summary>
public class ItemSettings
{
    public string GoTo { get; set; } = "Next Item";
    public string AfterCondition { get; set; } = "Manual";
    public int TransitionFadeTimeMs { get; set; } = 500;
}
