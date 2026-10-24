using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for the đợt 2 T7 slide-label foundation (<see cref="SlideLabelDefinition"/>,
/// <see cref="PresenterSettings.SlideLabels"/>, <see cref="Section.LabelId"/>,
/// <see cref="SlideLabelService"/>). Covers the T7 acceptance criteria: the six-label seed
/// for settings without a catalog, duplicate-hotkey validation (case-insensitive, empty
/// hotkeys ignored), resolution by LabelId, resolution by section-name fallback for
/// legacy data, a dangling LabelId (deleted label) resolving to no label without throwing,
/// and backward compatibility — old song/settings JSON load with LabelId/labels unset, and
/// a save+reload through <see cref="PresenterSettingsService"/> keeps a custom catalog.
/// </summary>
public class SlideLabelServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _dbPath;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true
    };

    public SlideLabelServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"SlideLabelTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _dbPath = Path.Combine(_tempFolder, "app.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempFolder))
        {
            try { Directory.Delete(_tempFolder, true); }
            catch (IOException) { /* best-effort temp cleanup */ }
        }
    }

    // ─── Seeding ─────────────────────────────────────────────────────

    [Fact]
    public void EnsureLabels_NullCatalog_SeedsExactlySixDefaults()
    {
        var settings = new PresenterSettings();
        Assert.Null(settings.SlideLabels);

        var labels = SlideLabelService.EnsureLabels(settings);

        Assert.Equal(6, labels.Count);
        Assert.Equal(
            new[] { "Verse 1", "Verse 2", "Verse 3", "Verse 4", "Chorus", "Bridge" },
            labels.Select(l => l.Name).ToArray());
        Assert.All(labels, l => Assert.Equal("", l.Hotkey));
        Assert.All(labels, l => Assert.False(string.IsNullOrWhiteSpace(l.Id)));
        Assert.Equal(6, labels.Select(l => l.Id).Distinct().Count());
        Assert.Equal(6, labels.Select(l => l.Color).Distinct().Count());
        Assert.Same(labels, settings.SlideLabels);
    }

    [Fact]
    public void EnsureLabels_IsIdempotent_AndNeverOverwritesAPresentCatalog()
    {
        var settings = new PresenterSettings();
        var first = SlideLabelService.EnsureLabels(settings);
        var second = SlideLabelService.EnsureLabels(settings);
        Assert.Same(first, second);
        Assert.Equal(6, second.Count);

        // A custom catalog is the user's own state — kept as-is.
        var custom = new List<SlideLabelDefinition>
        {
            new() { Id = "intro", Name = "Intro", Color = "#111111", Hotkey = "I" }
        };
        var customSettings = new PresenterSettings { SlideLabels = custom };
        Assert.Same(custom, SlideLabelService.EnsureLabels(customSettings));

        // Even an EMPTY catalog (user deleted every label) is not re-seeded.
        var emptySettings = new PresenterSettings { SlideLabels = new List<SlideLabelDefinition>() };
        Assert.Empty(SlideLabelService.EnsureLabels(emptySettings));
    }

    // ─── Hotkey validation ───────────────────────────────────────────

    [Fact]
    public void FindDuplicateHotkey_DuplicateIgnoringCase_IsReported()
    {
        var labels = new List<SlideLabelDefinition>
        {
            new() { Id = "a", Name = "A", Hotkey = "C" },
            new() { Id = "b", Name = "B", Hotkey = "c" }
        };

        Assert.NotNull(SlideLabelService.FindDuplicateHotkey(labels));
    }

    [Fact]
    public void FindDuplicateHotkey_EmptyOrWhitespaceHotkeys_NeverCountAsDuplicates()
    {
        var labels = new List<SlideLabelDefinition>
        {
            new() { Id = "a", Name = "A", Hotkey = "" },
            new() { Id = "b", Name = "B", Hotkey = "" },
            new() { Id = "c", Name = "C", Hotkey = "   " },
            new() { Id = "d", Name = "D", Hotkey = "" }
        };

        Assert.Null(SlideLabelService.FindDuplicateHotkey(labels));
    }

    [Fact]
    public void FindDuplicateHotkey_DistinctHotkeys_ReturnsNull()
    {
        var labels = new List<SlideLabelDefinition>
        {
            new() { Id = "a", Name = "A", Hotkey = "V" },
            new() { Id = "b", Name = "B", Hotkey = "C" },
            new() { Id = "c", Name = "C2", Hotkey = "" }
        };

        Assert.Null(SlideLabelService.FindDuplicateHotkey(labels));
        Assert.Null(SlideLabelService.FindDuplicateHotkey(null));
        Assert.Null(SlideLabelService.FindDuplicateHotkey(SlideLabelService.CreateDefaults()));
    }

    // ─── Resolution ──────────────────────────────────────────────────

    [Fact]
    public void Resolve_ByLabelId_ReturnsTheReferencedLabel()
    {
        var labels = SlideLabelService.CreateDefaults();
        var section = new Section { Name = "Anything", LabelId = "bridge" };

        var resolved = SlideLabelService.ResolveForSection(section, labels);

        Assert.NotNull(resolved);
        Assert.Equal("Bridge", resolved!.Name);
    }

    [Fact]
    public void Resolve_NoLabelId_FallsBackToSectionName()
    {
        var labels = SlideLabelService.CreateDefaults();

        var chorus = SlideLabelService.ResolveForSection(new Section { Name = "Chorus" }, labels);
        Assert.NotNull(chorus);
        Assert.Equal("chorus", chorus!.Id);

        // Case-insensitive and tolerant of surrounding whitespace (hand-typed legacy names).
        var lower = SlideLabelService.ResolveForSection(new Section { Name = "  chorus " }, labels);
        Assert.Same(chorus, lower);
    }

    [Fact]
    public void Resolve_DeletedLabel_ReturnsNull_WithoutThrowingOrNameFallback()
    {
        var labels = SlideLabelService.CreateDefaults();
        // The section still references a label id that was deleted from the catalog, and
        // its name happens to match a live label — the explicit (dangling) reference wins:
        // the section becomes unlabeled, per the R5 edge case.
        var section = new Section { Name = "Chorus", LabelId = "label-that-was-deleted" };

        var resolved = SlideLabelService.ResolveForSection(section, labels);

        Assert.Null(resolved);
    }

    [Fact]
    public void Resolve_UnknownNameOrNullInputs_ReturnsNull()
    {
        var labels = SlideLabelService.CreateDefaults();

        Assert.Null(SlideLabelService.ResolveForSection(new Section { Name = "Pre-Chorus" }, labels));
        Assert.Null(SlideLabelService.ResolveForSection(new Section { Name = "" }, labels));
        Assert.Null(SlideLabelService.ResolveForSection(null, labels));
        Assert.Null(SlideLabelService.ResolveForSection(new Section { Name = "Chorus" }, null));
        Assert.Null(SlideLabelService.ResolveForSection(
            new Section { Name = "Chorus" }, new List<SlideLabelDefinition>()));
    }

    // ─── Backward compatibility: settings JSON ───────────────────────

    [Fact]
    public void OldSettingsJson_WithoutSlideLabels_LoadsUnset_ThenSeeds()
    {
        const string oldJson = """{ "outputDisplay": 2, "linesPerSlide": 3 }""";

        var settings = JsonSerializer.Deserialize<PresenterSettings>(oldJson, JsonOptions);

        Assert.NotNull(settings);
        Assert.Equal(2, settings!.OutputDisplay);
        Assert.Null(settings.SlideLabels);

        var labels = SlideLabelService.EnsureLabels(settings);
        Assert.Equal(6, labels.Count);
    }

    [Fact]
    public void SettingsJson_RoundTrip_PreservesCatalog()
    {
        var settings = new PresenterSettings();
        var labels = SlideLabelService.EnsureLabels(settings);
        labels[4].Hotkey = "C"; // Chorus

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        Assert.Contains("slideLabels", json);

        var reloaded = JsonSerializer.Deserialize<PresenterSettings>(json, JsonOptions);
        Assert.NotNull(reloaded!.SlideLabels);
        Assert.Equal(6, reloaded.SlideLabels!.Count);
        Assert.Equal("C", reloaded.SlideLabels[4].Hotkey);
        Assert.Equal("Chorus", reloaded.SlideLabels[4].Name);
    }

    [Fact]
    public async Task SettingsService_LoadAsync_SeedsDefaults_OnEmptyDatabase()
    {
        var service = new PresenterSettingsService(_dbPath);
        await service.LoadAsync();

        Assert.NotNull(service.Settings.SlideLabels);
        Assert.Equal(6, service.Settings.SlideLabels!.Count);
    }

    [Fact]
    public async Task SettingsService_SaveAndReload_KeepsCustomCatalog()
    {
        var service = new PresenterSettingsService(_dbPath);
        await service.LoadAsync();
        service.Settings.SlideLabels!.Add(
            new SlideLabelDefinition { Id = "tag", Name = "Tag", Color = "#123456", Hotkey = "T" });
        await service.SaveAsync();

        var reloaded = new PresenterSettingsService(_dbPath);
        await reloaded.LoadAsync();

        Assert.Equal(7, reloaded.Settings.SlideLabels!.Count);
        var tag = reloaded.Settings.SlideLabels.Single(l => l.Id == "tag");
        Assert.Equal("T", tag.Hotkey);
    }

    // ─── Backward compatibility: song JSON ───────────────────────────

    [Fact]
    public void OldSongJson_SectionWithoutLabelId_LoadsWithNullLabelId_LoopFieldsIntact()
    {
        const string oldSongJson = """
        {
          "title": "Legacy Song",
          "sections": [
            { "name": "Verse 1", "loopIn_ms": 1000, "loopOut_ms": 9000, "startBar": 1, "endBar": 8, "canLoop": true },
            { "name": "Chorus", "loopIn_ms": 9000, "loopOut_ms": 17000, "startBar": 9, "endBar": 16, "canLoop": false }
          ]
        }
        """;

        var song = JsonSerializer.Deserialize<Song>(oldSongJson, JsonOptions);

        Assert.NotNull(song);
        Assert.Equal(2, song!.Sections.Count);
        Assert.All(song.Sections, s => Assert.Null(s.LabelId));
        // Loop/backtrack fields keep their exact meaning and values (R5.5).
        Assert.Equal(1000, song.Sections[0].LoopInMs);
        Assert.Equal(9000, song.Sections[0].LoopOutMs);
        Assert.True(song.Sections[0].CanLoop);
        Assert.False(song.Sections[1].CanLoop);

        // …and the legacy sections still resolve labels by name against the seeded catalog.
        var labels = SlideLabelService.CreateDefaults();
        Assert.Equal("Verse 1", SlideLabelService.ResolveForSection(song.Sections[0], labels)!.Name);
        Assert.Equal("Chorus", SlideLabelService.ResolveForSection(song.Sections[1], labels)!.Name);
    }

    [Fact]
    public void SongJson_RoundTrip_PreservesSectionLabelId()
    {
        var song = new Song
        {
            Title = "Labeled Song",
            Sections =
            {
                new Section { Name = "Verse 1", LabelId = "verse-1" },
                new Section { Name = "Free Section" }
            }
        };

        var json = JsonSerializer.Serialize(song, JsonOptions);
        var reloaded = JsonSerializer.Deserialize<Song>(json, JsonOptions);

        Assert.NotNull(reloaded);
        Assert.Equal("verse-1", reloaded!.Sections[0].LabelId);
        Assert.Null(reloaded.Sections[1].LabelId);
    }
}
