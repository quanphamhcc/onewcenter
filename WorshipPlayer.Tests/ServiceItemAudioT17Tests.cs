using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Đợt 2 T17 (R9): ServiceItem.Loop + ServiceItemAudio.ResolvePlayback +
/// ShouldPlayAttachedAudio. All pure; no audio hardware involved.
/// </summary>
public class ServiceItemAudioT17Tests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        // Mirror the ServiceOrderManager convention: nulls omitted, unknown keys ignored.
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public void ResolvePlayback_NewList_ReturnsAllFilesAndLoopFlag()
    {
        var item = new ServiceItem
        {
            AudioAttachments = new List<string> { "a.mp3", "b.wav" },
            Loop = true,
        };

        var resolved = ServiceItemAudio.ResolvePlayback(item);

        Assert.Equal(new List<string> { "a.mp3", "b.wav" }, resolved.Files);
        Assert.True(resolved.Loop);
    }

    [Fact]
    public void ResolvePlayback_LegacySinglePath_ReturnsOneElementAndLoopFalse()
    {
        var item = new ServiceItem
        {
            AudioAttachmentPath = "legacy.mp3",
            Loop = false,
        };

        var resolved = ServiceItemAudio.ResolvePlayback(item);

        Assert.Single(resolved.Files);
        Assert.Equal("legacy.mp3", resolved.Files[0]);
        Assert.False(resolved.Loop);
    }

    [Fact]
    public void ResolvePlayback_NoAudio_ReturnsEmptyAndLoopFalse()
    {
        var resolved = ServiceItemAudio.ResolvePlayback(new ServiceItem());

        Assert.Empty(resolved.Files);
        Assert.False(resolved.Loop);
    }

    [Fact]
    public void ResolvePlayback_NewListTakesPrecedenceOverLegacy()
    {
        var item = new ServiceItem
        {
            AudioAttachments = new List<string> { "new.mp3" },
            AudioAttachmentPath = "legacy.mp3",
        };

        var resolved = ServiceItemAudio.ResolvePlayback(item);

        Assert.Equal(new List<string> { "new.mp3" }, resolved.Files);
    }

    [Fact]
    public void Resolve_OldSignature_KeepsBehavior()
    {
        var item = new ServiceItem { AudioAttachments = new List<string> { "a.mp3" } };
        var files = ServiceItemAudio.Resolve(item);
        Assert.Equal(new List<string> { "a.mp3" }, files);

        var legacy = ServiceItemAudio.Resolve(new ServiceItem { AudioAttachmentPath = "x.mp3" });
        Assert.Equal(new List<string> { "x.mp3" }, legacy);

        Assert.Empty(ServiceItemAudio.Resolve(new ServiceItem()));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void JsonRoundTrip_LoopFlag_Preserved(bool loop)
    {
        var item = new ServiceItem
        {
            AudioAttachments = new List<string> { "a.mp3", "b.mp3" },
            Loop = loop,
        };

        var json = JsonSerializer.Serialize(item, JsonOptions);
        var back = JsonSerializer.Deserialize<ServiceItem>(json, JsonOptions);

        Assert.NotNull(back);
        Assert.Equal(loop, back!.Loop);
        Assert.Equal(item.AudioAttachments, back.AudioAttachments);
    }

    [Fact]
    public void Json_OldFileWithoutLoopKey_LoadsLoopFalse()
    {
        // Pre-T17 service files have no "loop" key; missing bool must stay false.
        var json = "{\"id\":\"1\",\"title\":\"Announcements\",\"audioAttachments\":[\"a.mp3\"]}";

        var item = JsonSerializer.Deserialize<ServiceItem>(json, JsonOptions);

        Assert.NotNull(item);
        Assert.False(item!.Loop);
        Assert.Equal(new List<string> { "a.mp3" }, item.AudioAttachments);
    }

    [Fact]
    public void Json_OldFileWithoutAnyAudioKeys_LoadsEmptyAndLoopFalse()
    {
        var json = "{\"id\":\"1\",\"title\":\"Welcome\"}";

        var item = JsonSerializer.Deserialize<ServiceItem>(json, JsonOptions);

        Assert.NotNull(item);
        Assert.False(item!.Loop);
        Assert.Null(item.AudioAttachments);
        Assert.Equal("", item.AudioAttachmentPath);
    }

    [Fact]
    public void ShouldPlayAttachedAudio_NullItem_ReturnsFalse()
    {
        Assert.False(ServiceItemAudio.ShouldPlayAttachedAudio(null));
    }

    [Fact]
    public void ShouldPlayAttachedAudio_SongWithBacktrack_ReturnsFalse_R94()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Song,
            UseBacktrack = true,
            AudioAttachments = new List<string> { "bg.mp3" },
        };

        Assert.False(ServiceItemAudio.ShouldPlayAttachedAudio(item));
    }

    [Fact]
    public void ShouldPlayAttachedAudio_SongWithoutBacktrack_ReturnsTrue()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Song,
            UseBacktrack = false,
            AudioAttachments = new List<string> { "bg.mp3" },
        };

        Assert.True(ServiceItemAudio.ShouldPlayAttachedAudio(item));
    }

    [Fact]
    public void ShouldPlayAttachedAudio_Announcement_ReturnsTrue()
    {
        var item = new ServiceItem
        {
            Type = ServiceItemType.Announcement,
            AudioAttachments = new List<string> { "bg.mp3" },
        };

        Assert.True(ServiceItemAudio.ShouldPlayAttachedAudio(item));
    }
}
