using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Win32;

namespace WorshipPlayer.Presenter.Views.Tools;

/// <summary>
/// CCLI song usage report dialog with date range filter and CSV export.
/// </summary>
public partial class CcliReportDialog : Window
{
    public ObservableCollection<CcliReportItem> ReportItems { get; } = new();

    public CcliReportDialog()
    {
        InitializeComponent();
        FromDatePicker.SelectedDate = DateTime.Now.AddMonths(-3);
        ToDatePicker.SelectedDate = DateTime.Now;
    }

    private void GenerateButton_Click(object sender, RoutedEventArgs e)
    {
        var fromDate = FromDatePicker.SelectedDate;
        var toDate = ToDatePicker.SelectedDate;

        if (fromDate == null || toDate == null)
        {
            MessageBox.Show("Please select both From and To dates.",
                "Date Range Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // In production, this would query the CcliReportService/SongUsageService.
        // For now, show the grid and enable export.
        ReportDataGrid.ItemsSource = ReportItems;
        ReportDataGrid.Visibility = Visibility.Visible;
        EmptyStateText.Visibility = ReportItems.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
        ExportCsvButton.IsEnabled = ReportItems.Count > 0;

        if (ReportItems.Count == 0)
        {
            EmptyStateText.Text = "No songs found for the selected date range.";
            EmptyStateText.Visibility = Visibility.Visible;
            ReportDataGrid.Visibility = Visibility.Collapsed;
        }
    }

    private void ExportCsvButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Filter = "CSV Files (*.csv)|*.csv",
            DefaultExt = "csv",
            FileName = $"CCLI_Report_{DateTime.Now:yyyyMMdd}.csv"
        };

        if (dialog.ShowDialog() == true)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Title,Artist,Times Played,Last Played");

            foreach (var item in ReportItems)
            {
                sb.AppendLine($"\"{item.Title}\",\"{item.Artist}\",{item.TimesPlayed},{item.LastPlayed:yyyy-MM-dd}");
            }

            File.WriteAllText(dialog.FileName, sb.ToString());
            MessageBox.Show("Report exported successfully.",
                "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
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
    public int TimesPlayed { get; set; }
    public DateTime LastPlayed { get; set; }
}
