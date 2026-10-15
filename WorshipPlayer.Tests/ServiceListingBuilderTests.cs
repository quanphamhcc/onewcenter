using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for ServiceListingBuilder: ordering (StartTime beats Date, unparseable
/// dates sort last, LastWriteTimeUtc tiebreak), projection, and filtering.
/// </summary>
public class ServiceListingBuilderTests
{
    private static readonly DateTime Now = new(2026, 3, 15, 12, 0, 0, DateTimeKind.Utc);

    private static ServiceFileInfo MakeInfo(
        string name,
        string type = "Sunday Morning",
        string date = "2026-03-01",
        DateTime? startTime = null,
        DateTime? lastWriteUtc = null,
        string? filePath = null)
    {
        var order = new ServiceOrder
        {
            Name = name,
            ServiceType = type,
            Date = date,
            StartTime = startTime
        };
        return new ServiceFileInfo(
            filePath ?? $"C:\\services\\{name}.json",
            lastWriteUtc ?? Now,
            order);
    }

    [Fact]
    public void Build_SortsByEffectiveDateDescending()
    {
        var files = new[]
        {
            MakeInfo("Older", date: "2026-01-01"),
            MakeInfo("Newer", date: "2026-02-01"),
        };

        var rows = ServiceListingBuilder.Build(files, Now);

        Assert.Equal("Newer", rows[0].Title);
        Assert.Equal("Older", rows[1].Title);
    }

    [Fact]
    public void Build_StartTimeBeatsDateForEffectiveDate()
    {
        // "WithTime" has an older stored Date but a newer StartTime; StartTime wins.
        var files = new[]
        {
            MakeInfo("DateOnly", date: "2026-02-01"),
            MakeInfo("WithTime", date: "2026-01-01",
                startTime: new DateTime(2026, 3, 1, 9, 0, 0)),
        };

        var rows = ServiceListingBuilder.Build(files, Now);

        Assert.Equal("WithTime", rows[0].Title);
        Assert.Equal("DateOnly", rows[1].Title);
    }

    [Fact]
    public void Build_UnparseableDateSortsLast()
    {
        var files = new[]
        {
            MakeInfo("Bad", date: "not-a-date"),
            MakeInfo("Good", date: "2026-02-01"),
        };

        var rows = ServiceListingBuilder.Build(files, Now);

        Assert.Equal("Good", rows[0].Title);
        Assert.Equal("Bad", rows[1].Title);
    }

    [Fact]
    public void Build_TiebreaksOnLastWriteTimeDescending()
    {
        var files = new[]
        {
            MakeInfo("OlderWrite", date: "2026-02-01",
                lastWriteUtc: new DateTime(2026, 2, 10, 8, 0, 0, DateTimeKind.Utc)),
            MakeInfo("NewerWrite", date: "2026-02-01",
                lastWriteUtc: new DateTime(2026, 2, 12, 8, 0, 0, DateTimeKind.Utc)),
        };

        var rows = ServiceListingBuilder.Build(files, Now);

        Assert.Equal("NewerWrite", rows[0].Title);
        Assert.Equal("OlderWrite", rows[1].Title);
    }

    [Fact]
    public void Build_ProjectsAllDisplayFields()
    {
        var startTime = new DateTime(2026, 2, 1, 8, 0, 0);
        var info = MakeInfo("Easter Service", type: "Special",
            date: "2026-02-01", startTime: startTime,
            filePath: "C:\\services\\easter.json");

        var rows = ServiceListingBuilder.Build(new[] { info }, Now);
        var row = Assert.Single(rows);

        Assert.Equal("Easter Service", row.Title);
        Assert.Equal("Special", row.Type);
        Assert.Equal(ServiceDateFormatter.FormatUs("2026-02-01", startTime), row.DateDisplay);
        Assert.Equal("Local", row.LastModifiedBy);
        Assert.Equal("C:\\services\\easter.json", row.FilePath);
    }

    [Fact]
    public void Filter_EmptyOrWhitespaceQuery_ReturnsAll()
    {
        var rows = ServiceListingBuilder.Build(new[]
        {
            MakeInfo("One"),
            MakeInfo("Two"),
        }, Now);

        Assert.Equal(2, ServiceListingBuilder.Filter(rows, "").Count());
        Assert.Equal(2, ServiceListingBuilder.Filter(rows, "   ").Count());
    }

    [Fact]
    public void Filter_CaseInsensitiveContainsOnTitle()
    {
        var rows = ServiceListingBuilder.Build(new[]
        {
            MakeInfo("Sunday Worship"),
            MakeInfo("Evening Prayer"),
        }, Now);

        var result = ServiceListingBuilder.Filter(rows, "WORSHIP").ToList();

        var row = Assert.Single(result);
        Assert.Equal("Sunday Worship", row.Title);
    }

    [Fact]
    public void Filter_MatchesOnType()
    {
        var rows = ServiceListingBuilder.Build(new[]
        {
            MakeInfo("One", type: "Sunday Morning"),
            MakeInfo("Two", type: "Youth Night"),
        }, Now);

        var result = ServiceListingBuilder.Filter(rows, "youth").ToList();

        var row = Assert.Single(result);
        Assert.Equal("Two", row.Title);
    }

    [Fact]
    public void Filter_TrimsQuery()
    {
        var rows = ServiceListingBuilder.Build(new[]
        {
            MakeInfo("Sunday Worship"),
            MakeInfo("Evening Prayer"),
        }, Now);

        var result = ServiceListingBuilder.Filter(rows, "  worship  ").ToList();

        Assert.Single(result);
    }
}
