using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.ServiceBuilder;

/// <summary>
/// Dialog for previewing, printing, and exporting the service order as text.
/// </summary>
public partial class PrintExportDialog : Window
{
    private IExportService? _exportService;
    private string _serviceTitle = string.Empty;
    private List<ServiceOrderEntry> _entries = new();

    public IExportService? ExportService
    {
        get => _exportService;
        set => _exportService = value;
    }

    public PrintExportDialog()
    {
        InitializeComponent();
    }

    public void LoadServiceOrder(string title, List<ServiceOrderEntry> entries)
    {
        _serviceTitle = title;
        _entries = entries;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        bool includeLyrics = IncludeLyricsCheckBox.IsChecked == true;
        var lines = new List<string>
        {
            _serviceTitle,
            new string('=', _serviceTitle.Length),
            string.Empty
        };

        foreach (var entry in _entries)
        {
            lines.Add($"- {entry.Title} ({entry.Type})");
            if (includeLyrics && !string.IsNullOrWhiteSpace(entry.Lyrics))
            {
                lines.Add(string.Empty);
                lines.Add(entry.Lyrics);
                lines.Add(string.Empty);
            }
        }

        PreviewTextBox.Text = string.Join(Environment.NewLine, lines);
    }

    private void IncludeLyrics_Changed(object sender, RoutedEventArgs e)
    {
        UpdatePreview();
    }

    private void Print_Click(object sender, RoutedEventArgs e)
    {
        var printDialog = new PrintDialog();
        if (printDialog.ShowDialog() == true)
        {
            var doc = new System.Windows.Documents.FlowDocument(
                new System.Windows.Documents.Paragraph(
                    new System.Windows.Documents.Run(PreviewTextBox.Text)));
            doc.PagePadding = new Thickness(50);
            printDialog.PrintDocument(
                ((System.Windows.Documents.IDocumentPaginatorSource)doc).DocumentPaginator,
                "Service Order");
        }
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Service Order",
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = SanitizeFileName(_serviceTitle) + ".txt"
        };

        if (dialog.ShowDialog() == true)
        {
            var path = dialog.FileName;
            // Validate path does not contain traversal
            if (path.Contains(".."))
            {
                MessageBox.Show("Invalid file path.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            File.WriteAllText(path, PreviewTextBox.Text);
            MessageBox.Show("Service order exported successfully.", "Export Complete",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(name.Select(c => invalid.Contains(c) ? '_' : c));
    }
}

/// <summary>
/// Represents a service order entry for print/export.
/// </summary>
public class ServiceOrderEntry
{
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string? Lyrics { get; set; }
}
