using System.Globalization;
using Xunit;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for RelativeTimeFormatter. "now" is pinned explicitly so the output is
/// deterministic regardless of the machine clock.
/// </summary>
public class RelativeTimeFormatterTests
{
    private static readonly DateTime Now = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void SubMinute_ReturnsJustNow()
    {
        var when = Now.AddSeconds(-30);
        Assert.Equal("just now", RelativeTimeFormatter.Format(when, Now));
    }

    [Fact]
    public void FutureTimestamp_ClockSkew_ReturnsJustNow()
    {
        var when = Now.AddMinutes(5);
        Assert.Equal("just now", RelativeTimeFormatter.Format(when, Now));
    }

    [Fact]
    public void OneMinute_ReturnsSingularMinuteAgo()
    {
        var when = Now.AddMinutes(-1);
        Assert.Equal("1 minute ago", RelativeTimeFormatter.Format(when, Now));
    }

    [Fact]
    public void FiftyNineMinutes_ReturnsMinutesAgo()
    {
        var when = Now.AddMinutes(-59);
        Assert.Equal("59 minutes ago", RelativeTimeFormatter.Format(when, Now));
    }

    [Fact]
    public void WithinTwentyFourHours_ReturnsYesterday()
    {
        var when = Now.AddHours(-5);
        Assert.Equal("yesterday", RelativeTimeFormatter.Format(when, Now));
    }

    [Fact]
    public void JustUnderOneDay_ReturnsYesterday()
    {
        var when = Now.AddHours(-23);
        Assert.Equal("yesterday", RelativeTimeFormatter.Format(when, Now));
    }

    [Fact]
    public void MultiDay_ReturnsDaysAgo()
    {
        var when = Now.AddDays(-3);
        Assert.Equal("3 days ago", RelativeTimeFormatter.Format(when, Now));
    }

    [Fact]
    public void OlderThanAWeek_ReturnsUsShortDate()
    {
        var when = Now.AddDays(-30);
        var expected = when.ToString(ServiceDateFormatter.UsDateFormat, CultureInfo.GetCultureInfo("en-US"));
        Assert.Equal(expected, RelativeTimeFormatter.Format(when, Now));
    }
}
