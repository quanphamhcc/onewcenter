using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services.Interfaces;

/// <summary>
/// Handles export operations for service orders and reports.
/// </summary>
public interface IExportService
{
    /// <summary>
    /// Export a service order as a printable text document.
    /// </summary>
    /// <param name="serviceOrder">Service order to export.</param>
    /// <param name="includeLyrics">Whether to include song lyrics in the export.</param>
    /// <param name="outputPath">File path to write the export to.</param>
    Task ExportServiceOrderTextAsync(ServiceOrder serviceOrder, bool includeLyrics, string outputPath);

    /// <summary>
    /// Export song usage report as CSV for CCLI reporting.
    /// </summary>
    /// <param name="records">Usage records to export.</param>
    /// <param name="outputPath">File path to write the CSV to.</param>
    Task ExportUsageReportCsvAsync(List<SongUsageRecord> records, string outputPath);

    /// <summary>
    /// Generate a printable view of the service order.
    /// </summary>
    /// <param name="serviceOrder">Service order to generate printable content for.</param>
    /// <param name="includeLyrics">Whether to include lyrics.</param>
    /// <returns>Formatted string content for printing.</returns>
    Task<string> GeneratePrintableContentAsync(ServiceOrder serviceOrder, bool includeLyrics);
}
