using System.Text.Json;
using Xunit;
using WorshipPlayer.Core.Models.Cloud;
using WorshipPlayer.Core.Services.Cloud;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for DesktopSyncService and Cloud model serialization.
/// </summary>
public class CloudSyncTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly DesktopSyncService _service;

    public CloudSyncTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"CloudSyncTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _service = new DesktopSyncService(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    // ─── RegisterSong and GetSyncStatus ────────────────────────────

    [Fact]
    public async Task RegisterSong_AddsToTracking()
    {
        _service.RegisterSong("song-1", Path.Combine(_tempFolder, "song1"), 1);

        var statuses = await _service.GetSyncStatusAsync();

        Assert.Single(statuses);
        Assert.Equal("song-1", statuses[0].SongId);
        Assert.Equal(SyncState.UpToDate, statuses[0].State);
        Assert.Equal(1, statuses[0].LocalVersion);
        Assert.Equal(1, statuses[0].RemoteVersion);
    }

    [Fact]
    public async Task RegisterSong_MultipleSongs_AllTracked()
    {
        _service.RegisterSong("song-1", Path.Combine(_tempFolder, "song1"), 1);
        _service.RegisterSong("song-2", Path.Combine(_tempFolder, "song2"), 2);
        _service.RegisterSong("song-3", Path.Combine(_tempFolder, "song3"), 3);

        var statuses = await _service.GetSyncStatusAsync();

        Assert.Equal(3, statuses.Count);
    }

    // ─── DetectLocalChanges ────────────────────────────────────────

    [Fact]
    public async Task DetectLocalChanges_MarksModifiedSongs()
    {
        var songPath = Path.Combine(_tempFolder, "song1");
        Directory.CreateDirectory(songPath);

        // Register with a sync time in the past
        _service.RegisterSong("song-1", songPath, 1);

        // Wait briefly to ensure file timestamp is after registration
        await Task.Delay(50);

        // Create a file (modification after sync time)
        File.WriteAllText(Path.Combine(songPath, "song.json"), "{}");

        _service.DetectLocalChanges();

        var statuses = await _service.GetSyncStatusAsync();
        Assert.Equal(SyncState.LocalChanges, statuses[0].State);
    }

    [Fact]
    public async Task DetectLocalChanges_UnmodifiedSongsStayUpToDate()
    {
        var songPath = Path.Combine(_tempFolder, "song1");
        Directory.CreateDirectory(songPath);

        // Create file first
        File.WriteAllText(Path.Combine(songPath, "song.json"), "{}");

        // Wait to ensure sync time is after file write
        await Task.Delay(50);

        _service.RegisterSong("song-1", songPath, 1);

        _service.DetectLocalChanges();

        var statuses = await _service.GetSyncStatusAsync();
        Assert.Equal(SyncState.UpToDate, statuses[0].State);
    }

    // ─── ResolveConflict ───────────────────────────────────────────

    [Fact]
    public async Task ResolveConflict_KeepLocal_SetsUpToDate()
    {
        _service.RegisterSong("song-1", Path.Combine(_tempFolder, "song1"), 2);

        // Manually set conflict state
        var statuses = await _service.GetSyncStatusAsync();
        statuses[0].State = SyncState.Conflict;
        statuses[0].ConflictDetails = "Both versions modified";
        statuses[0].RemoteVersion = 3;

        await _service.ResolveConflictAsync("song-1", ConflictResolution.KeepLocal);

        statuses = await _service.GetSyncStatusAsync();
        Assert.Equal(SyncState.UpToDate, statuses[0].State);
        Assert.Null(statuses[0].ConflictDetails);
        Assert.Equal(2, statuses[0].LocalVersion);
        Assert.Equal(2, statuses[0].RemoteVersion);
    }

    [Fact]
    public async Task ResolveConflict_KeepRemote_SetsUpToDate()
    {
        _service.RegisterSong("song-1", Path.Combine(_tempFolder, "song1"), 2);

        var statuses = await _service.GetSyncStatusAsync();
        statuses[0].State = SyncState.Conflict;
        statuses[0].RemoteVersion = 5;

        await _service.ResolveConflictAsync("song-1", ConflictResolution.KeepRemote);

        statuses = await _service.GetSyncStatusAsync();
        Assert.Equal(SyncState.UpToDate, statuses[0].State);
        Assert.Equal(5, statuses[0].LocalVersion);
        Assert.Equal(5, statuses[0].RemoteVersion);
    }

    [Fact]
    public async Task ResolveConflict_Merge_IncrementsVersion()
    {
        _service.RegisterSong("song-1", Path.Combine(_tempFolder, "song1"), 2);

        var statuses = await _service.GetSyncStatusAsync();
        statuses[0].State = SyncState.Conflict;
        statuses[0].RemoteVersion = 3;

        await _service.ResolveConflictAsync("song-1", ConflictResolution.Merge);

        statuses = await _service.GetSyncStatusAsync();
        Assert.Equal(SyncState.UpToDate, statuses[0].State);
        Assert.Equal(4, statuses[0].LocalVersion);
        Assert.Equal(4, statuses[0].RemoteVersion);
    }

    // ─── SyncAll ───────────────────────────────────────────────────

    [Fact]
    public async Task SyncAll_UpdatesAllSongStates()
    {
        _service.RegisterSong("song-1", Path.Combine(_tempFolder, "song1"), 1);
        _service.RegisterSong("song-2", Path.Combine(_tempFolder, "song2"), 1);

        // Set both to have local changes
        var statuses = await _service.GetSyncStatusAsync();
        statuses[0].State = SyncState.LocalChanges;
        statuses[1].State = SyncState.RemoteChanges;
        statuses[1].RemoteVersion = 2;

        await _service.SyncAllAsync();

        statuses = await _service.GetSyncStatusAsync();
        Assert.All(statuses, s => Assert.Equal(SyncState.UpToDate, s.State));
    }

    [Fact]
    public async Task SyncAll_SkipsUpToDateSongs()
    {
        _service.RegisterSong("song-1", Path.Combine(_tempFolder, "song1"), 1);

        // Already up to date - version should not change
        await _service.SyncAllAsync();

        var statuses = await _service.GetSyncStatusAsync();
        Assert.Equal(1, statuses[0].LocalVersion);
        Assert.Equal(1, statuses[0].RemoteVersion);
    }

    // ─── SetAutoSync ───────────────────────────────────────────────

    [Fact]
    public async Task SetAutoSync_StoresPreference()
    {
        Assert.False(_service.IsAutoSyncEnabled);

        await _service.SetAutoSyncAsync(true);
        Assert.True(_service.IsAutoSyncEnabled);

        await _service.SetAutoSyncAsync(false);
        Assert.False(_service.IsAutoSyncEnabled);
    }

    // ─── SyncProgressChanged event ────────────────────────────────

    [Fact]
    public async Task SyncProgressChanged_FiresDuringSync()
    {
        _service.RegisterSong("song-1", Path.Combine(_tempFolder, "song1"), 1);

        var statuses = await _service.GetSyncStatusAsync();
        statuses[0].State = SyncState.LocalChanges;

        var events = new List<SyncProgressEventArgs>();
        _service.SyncProgressChanged += (sender, args) => events.Add(args);

        await _service.SyncSongAsync("song-1");

        Assert.True(events.Count >= 1);
        Assert.Contains(events, e => e.SongId == "song-1");
        Assert.Contains(events, e => e.Progress == 100);
    }

    [Fact]
    public async Task SyncProgressChanged_FiresForEachSong_DuringSyncAll()
    {
        _service.RegisterSong("song-1", Path.Combine(_tempFolder, "song1"), 1);
        _service.RegisterSong("song-2", Path.Combine(_tempFolder, "song2"), 1);

        var statuses = await _service.GetSyncStatusAsync();
        statuses[0].State = SyncState.LocalChanges;
        statuses[1].State = SyncState.RemoteChanges;
        statuses[1].RemoteVersion = 2;

        var events = new List<SyncProgressEventArgs>();
        _service.SyncProgressChanged += (sender, args) => events.Add(args);

        await _service.SyncAllAsync();

        Assert.Contains(events, e => e.SongId == "song-1");
        Assert.Contains(events, e => e.SongId == "song-2");
    }

    // ─── JSON serialization round-trip tests ───────────────────────

    [Fact]
    public void SongLibraryItem_SerializesRoundTrip()
    {
        var item = new SongLibraryItem
        {
            Id = "test-123",
            Title = "Amazing Grace",
            Artist = "John Newton",
            Key = "G",
            Bpm = 72.0,
            Tags = new List<string> { "hymn", "classic", "worship" },
            CcliNumber = "4669732",
            HasBacktracks = true,
            HasLyrics = true,
            ThumbnailUrl = "https://example.com/thumb.jpg",
            CreatedAt = new DateTime(2024, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2024, 6, 20, 14, 0, 0, DateTimeKind.Utc),
            Version = 3
        };

        var json = JsonSerializer.Serialize(item);
        var deserialized = JsonSerializer.Deserialize<SongLibraryItem>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(item.Id, deserialized.Id);
        Assert.Equal(item.Title, deserialized.Title);
        Assert.Equal(item.Artist, deserialized.Artist);
        Assert.Equal(item.Key, deserialized.Key);
        Assert.Equal(item.Bpm, deserialized.Bpm);
        Assert.Equal(item.Tags, deserialized.Tags);
        Assert.Equal(item.CcliNumber, deserialized.CcliNumber);
        Assert.Equal(item.HasBacktracks, deserialized.HasBacktracks);
        Assert.Equal(item.HasLyrics, deserialized.HasLyrics);
        Assert.Equal(item.ThumbnailUrl, deserialized.ThumbnailUrl);
        Assert.Equal(item.Version, deserialized.Version);
    }

    [Fact]
    public void SyncInfo_SerializesRoundTrip()
    {
        var info = new SyncInfo
        {
            SongId = "song-456",
            LastSyncTime = new DateTime(2024, 3, 10, 8, 0, 0, DateTimeKind.Utc),
            State = SyncState.Conflict,
            ConflictDetails = "Both versions modified lyrics",
            LocalVersion = 4,
            RemoteVersion = 5
        };

        var json = JsonSerializer.Serialize(info);
        var deserialized = JsonSerializer.Deserialize<SyncInfo>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(info.SongId, deserialized.SongId);
        Assert.Equal(info.LastSyncTime, deserialized.LastSyncTime);
        Assert.Equal(info.State, deserialized.State);
        Assert.Equal(info.ConflictDetails, deserialized.ConflictDetails);
        Assert.Equal(info.LocalVersion, deserialized.LocalVersion);
        Assert.Equal(info.RemoteVersion, deserialized.RemoteVersion);
    }

    [Fact]
    public void DownloadRequest_SerializesRoundTrip()
    {
        var request = new DownloadRequest
        {
            SongId = "song-789",
            IncludeBacktracks = true,
            IncludeLyrics = true,
            IncludeChordChart = false,
            TargetFolder = "/songs/downloaded"
        };

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<DownloadRequest>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(request.SongId, deserialized.SongId);
        Assert.Equal(request.IncludeBacktracks, deserialized.IncludeBacktracks);
        Assert.Equal(request.IncludeLyrics, deserialized.IncludeLyrics);
        Assert.Equal(request.IncludeChordChart, deserialized.IncludeChordChart);
        Assert.Equal(request.TargetFolder, deserialized.TargetFolder);
    }

    [Fact]
    public void ServicePlanDto_SerializesRoundTrip()
    {
        var plan = new ServicePlanDto
        {
            Id = "plan-001",
            Name = "Sunday Morning Service",
            Date = "2024-07-14",
            ServiceType = "Sunday Morning",
            Items = new List<ServicePlanItemDto>
            {
                new ServicePlanItemDto
                {
                    Id = "item-1",
                    SongId = "song-123",
                    Type = "Song",
                    Title = "Amazing Grace",
                    KeyShift = 2,
                    TempoPercent = 95.0,
                    Notes = "Start soft",
                    Order = 1
                },
                new ServicePlanItemDto
                {
                    Id = "item-2",
                    SongId = null,
                    Type = "Scripture",
                    Title = "Romans 8:28",
                    KeyShift = 0,
                    TempoPercent = 100.0,
                    Notes = "",
                    Order = 2
                }
            }
        };

        var json = JsonSerializer.Serialize(plan);
        var deserialized = JsonSerializer.Deserialize<ServicePlanDto>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(plan.Id, deserialized.Id);
        Assert.Equal(plan.Name, deserialized.Name);
        Assert.Equal(plan.Date, deserialized.Date);
        Assert.Equal(plan.ServiceType, deserialized.ServiceType);
        Assert.Equal(2, deserialized.Items.Count);
        Assert.Equal("song-123", deserialized.Items[0].SongId);
        Assert.Null(deserialized.Items[1].SongId);
        Assert.Equal(2, deserialized.Items[0].KeyShift);
        Assert.Equal(95.0, deserialized.Items[0].TempoPercent);
    }

    [Fact]
    public void SyncState_SerializesAsString()
    {
        var info = new SyncInfo { SongId = "test", State = SyncState.Downloading };

        var json = JsonSerializer.Serialize(info);

        Assert.Contains("\"Downloading\"", json);
    }
}
