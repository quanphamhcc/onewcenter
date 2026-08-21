using System.Text;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Handles exporting service orders as printable text and usage reports as CSV.
/// </summary>
public class ExportService : IExportService
{
    /// <inheritdoc />
    public async Task ExportServiceOrderTextAsync(ServiceOrder serviceOrder, bool includeLyrics, string outputPath)
    {
        var content = await GeneratePrintableContentAsync(serviceOrder, includeLyrics);

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        await File.WriteAllTextAsync(outputPath, content);
    }

    /// <inheritdoc />
    public Task ExportUsageReportCsvAsync(List<SongUsageRecord> records, string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Song Title,Song ID,Artist,CCLI Number,Service Name,Service Date,Date Played");

        foreach (var record in records)
        {
            sb.AppendLine($"\"{EscapeCsv(record.SongTitle)}\",\"{EscapeCsv(record.SongId)}\",\"{EscapeCsv(record.Artist)}\",\"{EscapeCsv(record.CcliNumber)}\",\"{EscapeCsv(record.ServiceName)}\",\"{record.ServiceDate:yyyy-MM-dd}\",\"{record.DatePlayed:yyyy-MM-dd HH:mm:ss}\"");
        }

        var dir = Path.GetDirectoryName(outputPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        return File.WriteAllTextAsync(outputPath, sb.ToString());
    }

    /// <inheritdoc />
    public Task<string> GeneratePrintableContentAsync(ServiceOrder serviceOrder, bool includeLyrics)
    {
        var sb = new StringBuilder();

        // Header
        sb.AppendLine("═══════════════════════════════════════════════════════════");
        sb.AppendLine($"  SERVICE ORDER: {serviceOrder.Name}");
        sb.AppendLine($"  Date: {serviceOrder.Date}");
        sb.AppendLine($"  Type: {serviceOrder.ServiceType}");
        sb.AppendLine("═══════════════════════════════════════════════════════════");
        sb.AppendLine();

        // Items
        int itemNumber = 1;
        foreach (var item in serviceOrder.Items)
        {
            sb.AppendLine($"  {itemNumber}. [{item.Type}] {item.Title}");

            if (!string.IsNullOrWhiteSpace(item.Subtitle))
                sb.AppendLine($"     {item.Subtitle}");

            if (!string.IsNullOrWhiteSpace(item.Notes))
                sb.AppendLine($"     Notes: {item.Notes}");

            if (item.DurationMinutes > 0)
                sb.AppendLine($"     Duration: {item.DurationMinutes} min");

            if (item.Type == ServiceItemType.Scripture && !string.IsNullOrWhiteSpace(item.ScriptureReference))
                sb.AppendLine($"     Reference: {item.ScriptureReference} ({item.ScriptureTranslation})");

            if (includeLyrics && item.Slides.Count > 0)
            {
                sb.AppendLine();
                foreach (var slide in item.Slides)
                {
                    if (!string.IsNullOrWhiteSpace(slide.Content.Text))
                    {
                        var lines = slide.Content.Text.Split('\n');
                        foreach (var line in lines)
                        {
                            sb.AppendLine($"       {line}");
                        }
                        sb.AppendLine();
                    }
                }
            }

            sb.AppendLine("  ───────────────────────────────────────────────────────");
            itemNumber++;
        }

        // Footer
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(serviceOrder.Notes))
        {
            sb.AppendLine("  NOTES:");
            sb.AppendLine($"  {serviceOrder.Notes}");
            sb.AppendLine();
        }

        sb.AppendLine($"  Total Items: {serviceOrder.Items.Count}");
        sb.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm}");

        return Task.FromResult(sb.ToString());
    }

    private static string EscapeCsv(string value)
    {
        return value?.Replace("\"", "\"\"") ?? "";
    }
}
