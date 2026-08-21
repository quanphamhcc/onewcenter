using System.Windows;
using System.Windows.Controls;

namespace WorshipPlayer.Presenter.Views.ServiceBuilder;

/// <summary>
/// Panel for bulk editing multiple selected service items.
/// Appears when multiple items are selected in the service sidebar.
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
        var options = new BulkEditOptions
        {
            ApplyBackground = ApplyBackgroundCheck.IsChecked == true,
            BackgroundType = GetComboBoxText(BackgroundComboBox),
            ApplyTransition = ApplyTransitionCheck.IsChecked == true,
            TransitionTimeMs = (int)BatchTransitionSlider.Value,
            ApplyAutoAdvance = ApplyAutoAdvanceCheck.IsChecked == true,
            AutoAdvanceMode = GetComboBoxText(AutoAdvanceComboBox)
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

/// <summary>
/// Options for a bulk edit operation.
/// </summary>
public class BulkEditOptions
{
    public bool ApplyBackground { get; set; }
    public string BackgroundType { get; set; } = string.Empty;
    public bool ApplyTransition { get; set; }
    public int TransitionTimeMs { get; set; }
    public bool ApplyAutoAdvance { get; set; }
    public string AutoAdvanceMode { get; set; } = string.Empty;
}
