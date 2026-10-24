using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Views.ServiceBuilder;

/// <summary>
/// Panel for bulk editing multiple selected service items (roadmap C3).
/// Hosted modally by <see cref="BulkEditWindow"/>. Each field is opt-in via a checkbox; the
/// panel builds the Core <see cref="BulkEditOptions"/> (reusing <see cref="BulkEditService"/>'s
/// label→enum mapping) so the pure batch-apply logic stays testable and shared with C4.
/// </summary>
public partial class BulkEditPanel : UserControl
{
    private int _selectedCount;

    public event EventHandler<BulkEditOptions>? ApplyRequested;
    public event EventHandler? DeleteRequested;

    public BulkEditPanel()
    {
        InitializeComponent();
        BatchTransitionSlider.ValueChanged += (_, e) =>
        {
            if (BatchTransitionValue != null)
                BatchTransitionValue.Text = $"{(int)e.NewValue} ms";
        };
    }

    public void SetSelectedCount(int count)
    {
        _selectedCount = count;
        HeaderText.Text = $"Bulk Edit ({count} items selected)";
        ApplyButton.Content = $"Apply to {count} items";

        if (count > 1)
        {
            EmptyStateText.Visibility = Visibility.Collapsed;
            BatchOptionsPanel.Visibility = Visibility.Visible;
        }
        else
        {
            EmptyStateText.Visibility = Visibility.Visible;
            BatchOptionsPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void Apply_Click(object sender, RoutedEventArgs e)
    {
        // Build the Core options directly, mapping the human-readable combo text through the
        // shared Core parsers so the panel and the pure apply logic agree.
        var options = new BulkEditOptions
        {
            ApplyBackground = ApplyBackgroundCheck.IsChecked == true,
            BackgroundType = BulkEditService.ParseBackground(GetComboBoxText(BackgroundComboBox)),
            // Đợt 2 T2 (R2): "Inherit Background" choice → set BackgroundKind=Inherit
            // on the selected items (legacy BackgroundType left untouched).
            ApplyBackgroundInherit = ApplyBackgroundCheck.IsChecked == true
                && GetComboBoxText(BackgroundComboBox) == "Inherit Background",
            ApplyTransition = ApplyTransitionCheck.IsChecked == true,
            TransitionTimeMs = (int)BatchTransitionSlider.Value,
            ApplyAutoAdvance = ApplyAutoAdvanceCheck.IsChecked == true,
            AutoAdvanceCondition = BulkEditService.ParseAfterCondition(GetComboBoxText(AutoAdvanceComboBox))
        };

        ApplyRequested?.Invoke(this, options);
    }

    private void DeleteSelected_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            $"Delete {_selectedCount} selected items?",
            "Confirm Bulk Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private static string GetComboBoxText(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
            return item.Content?.ToString() ?? string.Empty;
        return string.Empty;
    }
}
