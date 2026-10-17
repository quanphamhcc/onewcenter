using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Win32;

namespace WorshipPlayer.Presenter.Views.Tools;

/// <summary>
/// CCLI song usage report dialog with date range filter and CSV export.
/// Data load and CSV export are routed through the host (OperatorWindow) via the
/// <see cref="QueryAsync"/> / <see cref="ExportAsync"/> callbacks, which are backed by
/// WorshipPlayer.Core CcliReportService (GetReportAsync / ExportCsvAsync).
/// </summary>
public partial class CcliReportDialog : Window
{
    public ObservableCollection<CcliReportItem> ReportItems { get; } = new();

    /// <summary>
    /// Query the CCLI report for a date range. Set by the host before the dialog is shown.
    /// Returns the rows for the given [from, to] window (inclusive).
    /// </summary>
    public Func<DateTime, DateTime, Task<List<CcliReportItem>>>? QueryAsync { get; set; }

    /// <summary>
    /// Export the CCLI report for a date range to a CSV file. Set by the host before the dialog
    /// is shown. Arguments: from, to, output path.
    /// </summary>
    public Func<DateTime, DateTime, string, Task>? ExportAsync { get; set; }

    public CcliReportDialog()
    {
        InitializeComponent();
        FromDatePicker.SelectedDate = DateTime.Now.AddMonths(-3);
        ToDatePicker.SelectedDate = DateTime.Now;
    }

    private async void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        var fromDate = FromDatePicker.SelectedDate;
        var toDate = ToDatePicker.SelectedDate;

        if (fromDate == null || toDate == null)
        {
            MessageBox.Show("Please select both From and To dates.",
                "Date Range Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        await LoadForRangeAsync(fromDate.Value, toDate.Value);
    }

    /// <summary>
    /// Query the report for the selected range through the host callback and repopulate the grid.
    /// </summary>
    public async Task LoadForRangeAsync(DateTime from, DateTime to)
    {
        if (QueryAsync == null)
        {
            // No data source wired — show empty state rather than stale data.
            SetReportData(new List<CcliReportItem>());
            return;
        }

        try
        {
            var items = await QueryAsync(from, to);
            SetReportData(items);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CCLI report query failed: {ex.Message}");
            SetReportData(new List<CcliReportItem>());
        }
    }

    private async void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        var fromDate = FromDatePicker.SelectedDate;
        var toDate = ToDatePicker.SelectedDate;

        if (fromDate == null || toDate == null)
        {
            MessageBox.Show("Please select both From and To dates.",
                "Date Range Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (ExportAsync == null)
        {
            MessageBox.Show("Export is not available.",
                "Export", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var dialog = new SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv",
            DefaultExt = "csv",
            FileName = $"CCLI_Report_{DateTime.Now:yyyyMMdd}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            try
            {
                // Route export through CcliReportService.ExportCsvAsync — includes the CCLI Number
                // column, honors the selected date range, and applies CSV escaping.
                await ExportAsync(fromDate.Value, toDate.Value, dialog.FileName);
                MessageBox.Show("Report exported successfully.",
                    "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export report: {ex.Message}",
                    "Export", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    /// <summary>
    /// Populate report data from external source.
    /// </summary>
    public void SetReportData(IEnumerable<CcliReportItem> items)
    {
        ReportItems.Clear();
        foreach (var item in items)
            ReportItems.Add(item);

        ReportDataGrid.ItemsSource = ReportItems;
        ReportDataGrid.Visibility = ReportItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateText.Visibility = ReportItems.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        if (ReportItems.Count == 0)
            EmptyStateText.Text = "No songs found for the selected date range.";
        ExportCsvButton.IsEnabled = ReportItems.Count > 0;
    }
}

/// <summary>
/// Display model for a CCLI report row.
/// </summary>
public class CcliReportItem
{
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string CcliNumber { get; set; } = string.Empty;
    public int TimesPlayed { get; set; }
    public DateTime LastPlayed { get; set; }
}
