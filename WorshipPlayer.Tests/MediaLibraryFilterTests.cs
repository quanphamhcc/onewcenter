using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for MediaLibraryFilter — the pure client-side composition of the
/// Media panel's type/tag/search filters (roadmap F3). Cross-platform (net8.0, no WPF).
/// </summary>
public class MediaLibraryFilterTests
{
    private static List<MediaLibraryItem> SampleItems() => new()
    {
        new MediaLibraryItem { Name = "Sunset", MediaType = MediaLibraryType.Image, Tags = new() { "Nature", "Warm" } },
        new MediaLibraryItem { Name = "Mountains", MediaType = MediaLibraryType.Image, Tags = new() { "Nature" } },
        new MediaLibraryItem { Name = "Welcome Loop", MediaType = MediaLibraryType.Video, Tags = new() { "Opener", "Warm" } },
        new MediaLibraryItem { Name = "Ambient Pad", MediaType = MediaLibraryType.Audio, Tags = new() { "Bed" } },
        new MediaLibraryItem { Name = "Untagged Clip", MediaType = MediaLibraryType.Video, Tags = new() },
    };

    [Fact]
    public void Apply_NoFilters_ReturnsAll()
    {
        var items = SampleItems();
        var result = MediaLibraryFilter.Apply(items, null, null, null);
        Assert.Equal(items.Count, result.Count);
    }

    [Fact]
    public void Apply_TypeOnly_Image()
    {
        var result = MediaLibraryFilter.Apply(SampleItems(), MediaLibraryType.Image, null, null);
        Assert.Equal(2, result.Count);
        Assert.All(result, i => Assert.Equal(MediaLibraryType.Image, i.MediaType));
    }

    [Fact]
    public void Apply_TypeOnly_Video()
    {
        var result = MediaLibraryFilter.Apply(SampleItems(), MediaLibraryType.Video, null, null);
        Assert.Equal(2, result.Count);
        Assert.All(result, i => Assert.Equal(MediaLibraryType.Video, i.MediaType));
    }

    [Fact]
    public void Apply_TypeOnly_Audio()
    {
        var result = MediaLibraryFilter.Apply(SampleItems(), MediaLibraryType.Audio, null, null);
        var item = Assert.Single(result);
        Assert.Equal("Ambient Pad", item.Name);
    }

    [Fact]
    public void Apply_TagOnly_IsCaseInsensitive()
    {
        var result = MediaLibraryFilter.Apply(SampleItems(), null, "nature", null);
        Assert.Equal(2, result.Count);
        Assert.All(result, i => Assert.Contains("Nature", i.Tags));
    }

    [Fact]
    public void Apply_TagOnly_NoMatch_ReturnsEmpty()
    {
        var result = MediaLibraryFilter.Apply(SampleItems(), null, "Nonexistent", null);
        Assert.Empty(result);
    }

    [Fact]
    public void Apply_SearchMatchesName()
    {
        var result = MediaLibraryFilter.Apply(SampleItems(), null, null, "mount");
        var item = Assert.Single(result);
        Assert.Equal("Mountains", item.Name);
    }

    [Fact]
    public void Apply_SearchMatchesTag()
    {
        var result = MediaLibraryFilter.Apply(SampleItems(), null, null, "warm");
        Assert.Equal(2, result.Count);
        Assert.Contains(result, i => i.Name == "Sunset");
        Assert.Contains(result, i => i.Name == "Welcome Loop");
    }

    [Fact]
    public void Apply_CombinedTypeTagSearch_ReturnsIntersection()
    {
        // Image AND tag "Warm" AND search "sun" -> only Sunset.
        var result = MediaLibraryFilter.Apply(SampleItems(), MediaLibraryType.Image, "Warm", "sun");
        var item = Assert.Single(result);
        Assert.Equal("Sunset", item.Name);
    }

    [Fact]
    public void Apply_CombinedTypeTag_NoIntersection_ReturnsEmpty()
    {
        // Audio type has no "Warm" tag.
        var result = MediaLibraryFilter.Apply(SampleItems(), MediaLibraryType.Audio, "Warm", null);
        Assert.Empty(result);
    }

    [Fact]
    public void Apply_WhitespaceInputs_TreatedAsNoOp()
    {
        var items = SampleItems();
        var result = MediaLibraryFilter.Apply(items, null, "   ", "  ");
        Assert.Equal(items.Count, result.Count);
    }

    [Fact]
    public void Apply_NullItems_ReturnsEmpty()
    {
        var result = MediaLibraryFilter.Apply(null!, MediaLibraryType.Image, null, null);
        Assert.Empty(result);
    }

    [Fact]
    public void DistinctTags_ReturnsSortedDistinctIgnoringCase()
    {
        var items = new List<MediaLibraryItem>
        {
            new MediaLibraryItem { Tags = new() { "Warm", "nature" } },
            new MediaLibraryItem { Tags = new() { "Nature", "Opener" } },
            new MediaLibraryItem { Tags = new() { "  " } },
        };

        var result = MediaLibraryFilter.DistinctTags(items);

        Assert.Equal(new[] { "nature", "Opener", "Warm" }, result);
    }

    [Fact]
    public void DistinctTags_NullItems_ReturnsEmpty()
    {
        var result = MediaLibraryFilter.DistinctTags(null!);
        Assert.Empty(result);
    }
}
