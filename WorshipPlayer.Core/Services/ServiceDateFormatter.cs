using System.Globalization;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// US-format (MM/dd/yyyy, 12-hour AM/PM) parsing and formatting for the service
/// date/time shown in the service-info UI. Always uses the en-US culture explicitly
/// so a non-US OS locale (e.g. vi-VN) still renders MM/DD/YYYY and AM/PM.
///
/// Storage contract (unchanged by this helper):
///   - ServiceOrder.Date stays a date-only string in "yyyy-MM-dd" so the derived
///     filename and the date sort keep working.
///   - ServiceOrder.StartTime is the optional date+time (null = date-only service).
/// </summary>
public static class ServiceDateFormatter
{
    private static readonly CultureInfo UsCulture = CultureInfo.GetCultureInfo("en-US");

    /// <summary>Canonical storage format for the date-only string.</summary>
    public const string StorageDateFormat = "yyyy-MM-dd";

    /// <summary>US display format when a time is present.</summary>
    public const string UsDateTimeFormat = "MM/dd/yyyy hh:mm tt";

    /// <summary>US display format when only a date is present.</summary>
    public const string UsDateFormat = "MM/dd/yyyy";

    /// <summary>
    /// Format the stored date (+ optional start time) as a US string:
    /// "MM/dd/yyyy hh:mm tt" when <paramref name="startTime"/> has a value,
    /// otherwise "MM/dd/yyyy". The stored <paramref name="date"/> is parsed
    /// tolerantly (accepts "yyyy-MM-dd" and "MM/dd/yyyy").
    /// </summary>
    public static string FormatUs(string? date, DateTime? startTime)
    {
        if (startTime.HasValue)
            return startTime.Value.ToString(UsDateTimeFormat, UsCulture);

        if (TryParseStoredDate(date, out var parsed))
            return parsed.ToString(UsDateFormat, UsCulture);

        // Fall back to whatever was stored rather than throwing.
        return date ?? string.Empty;
    }

    /// <summary>
    /// Parse a user-entered US string that is a date "MM/dd/yyyy" with an optional
    /// trailing time " hh:mm tt" (e.g. "10/18/2026 08:00 AM" or "10/18/2026").
    /// On success, <paramref name="date"/> is normalized to "yyyy-MM-dd" (so the
    /// filename/sort logic is unchanged) and <paramref name="startTime"/> is the full
    /// date+time when a time was supplied, else null.
    /// </summary>
    public static bool TryParseUs(string? input, out string date, out DateTime? startTime)
    {
        date = string.Empty;
        startTime = null;

        if (string.IsNullOrWhiteSpace(input))
            return false;

        var text = input.Trim();

        // Try date + time first, then date only.
        if (DateTime.TryParseExact(text, UsDateTimeFormat, UsCulture,
                DateTimeStyles.None, out var dt))
        {
            date = dt.ToString(StorageDateFormat, UsCulture);
            startTime = dt;
            return true;
        }

        if (DateTime.TryParseExact(text, UsDateFormat, UsCulture,
                DateTimeStyles.None, out var dateOnly))
        {
            date = dateOnly.ToString(StorageDateFormat, UsCulture);
            startTime = null;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Parse just a time-of-day entered in US 12-hour form ("hh:mm tt", e.g.
    /// "08:00 AM"). Also accepts "h:mm tt" / 24-hour "HH:mm" as a convenience.
    /// Returns the <see cref="TimeSpan"/> on success.
    /// </summary>
    public static bool TryParseTime(string? input, out TimeSpan time)
    {
        time = TimeSpan.Zero;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var text = input.Trim();
        string[] formats = { "hh:mm tt", "h:mm tt", "HH:mm", "H:mm" };
        if (DateTime.TryParseExact(text, formats, UsCulture,
                DateTimeStyles.None, out var dt))
        {
            time = dt.TimeOfDay;
            return true;
        }

        return false;
    }

    /// <summary>Format a time-of-day as US 12-hour "hh:mm tt" (e.g. "08:00 AM").</summary>
    public static string FormatTime(TimeSpan time)
        => DateTime.Today.Add(time).ToString("hh:mm tt", UsCulture);

    /// <summary>
    /// Tolerant parse of the stored date string. Accepts the canonical
    /// "yyyy-MM-dd", the US "MM/dd/yyyy", or any culture-invariant/en-US parseable value.
    /// </summary>
    public static bool TryParseStoredDate(string? date, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(date))
            return false;

        var text = date.Trim();
        if (DateTime.TryParseExact(text, StorageDateFormat, UsCulture,
                DateTimeStyles.None, out result))
            return true;

        if (DateTime.TryParseExact(text, UsDateFormat, UsCulture,
                DateTimeStyles.None, out result))
            return true;

        // Last resort: en-US general parse (handles e.g. date+time stored strings).
        return DateTime.TryParse(text, UsCulture, DateTimeStyles.None, out result);
    }
}
