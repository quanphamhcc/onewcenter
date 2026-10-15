using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Builds and filters the presentational rows for the services list. Sorts the scanned
/// files first (newest effective date first) then projects to <see cref="ServiceListRow"/>,
/// so the rows carry display strings only.
/// </summary>
public static class ServiceListingBuilder
{
    // Sort-last sentinel for files whose date cannot be determined.
    private static readonly DateTime SortLast = DateTime.MinValue;

    /// <summary>
    /// Sort the scanned files by effective date (StartTime if present, else the parsed
    /// stored Date, else sort-last), tiebreaking on last-write time, then project to rows.
    /// </summary>
    public static List<ServiceListRow> Build(IEnumerable<ServiceFileInfo> files, DateTime nowUtc)
    {
        return files
            .OrderByDescending(EffectiveDate)
            .ThenByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new ServiceListRow
            {
                Title = f.Order.Name,
                Type = f.Order.ServiceType,
                DateDisplay = ServiceDateFormatter.FormatUs(f.Order.Date, f.Order.StartTime),
                LastModifiedDisplay = RelativeTimeFormatter.Format(f.LastWriteTimeUtc, nowUtc),
                LastModifiedBy = "Local",
                FilePath = f.FilePath
            })
            .ToList();
    }

    /// <summary>
    /// Filter rows by a case-insensitive Contains match on Title OR Type. A null, empty,
    /// or whitespace query returns all rows. The query is trimmed before matching.
    /// </summary>
    public static IEnumerable<ServiceListRow> Filter(IEnumerable<ServiceListRow> rows, string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return rows;

        var trimmed = query.Trim();
        return rows.Where(r =>
            r.Title.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
            r.Type.Contains(trimmed, StringComparison.OrdinalIgnoreCase));
    }

    private static DateTime EffectiveDate(ServiceFileInfo info)
    {
        if (info.Order.StartTime.HasValue)
            return info.Order.StartTime.Value;

        return ServiceDateFormatter.TryParseStoredDate(info.Order.Date, out var d) ? d : SortLast;
    }
}
