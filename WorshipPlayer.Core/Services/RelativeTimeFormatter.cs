using System.Globalization;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Formats a UTC timestamp relative to "now" for the services list "last modified"
/// column: "just now", "N minutes ago", "yesterday", "N days ago", or a US short date
/// for older timestamps. Pure and deterministic — "now" is passed in so tests can pin it.
/// </summary>
public static class RelativeTimeFormatter
{
    private static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>
    /// Format <paramref name="whenUtc"/> relative to <paramref name="nowUtc"/>.
    /// A future timestamp (clock skew) and anything under a minute reads "just now".
    /// </summary>
    public static string Format(DateTime whenUtc, DateTime nowUtc)
    {
        var delta = nowUtc - whenUtc;

        // Clock-skew guard: a future timestamp reads as "just now".
        if (delta < TimeSpan.Zero)
            return "just now";

        if (delta < TimeSpan.FromMinutes(1))
            return "just now";

        if (delta < TimeSpan.FromHours(1))
        {
            int minutes = (int)delta.TotalMinutes;
            return $"{minutes} minute{(minutes == 1 ? "" : "s")} ago";
        }

        if (delta < TimeSpan.FromHours(24))
            return "yesterday";

        if (delta < TimeSpan.FromDays(7))
        {
            int days = (int)delta.TotalDays;
            return $"{days} day{(days == 1 ? "" : "s")} ago";
        }

        return whenUtc.ToString(ServiceDateFormatter.UsDateFormat, UsCulture);
    }
}
