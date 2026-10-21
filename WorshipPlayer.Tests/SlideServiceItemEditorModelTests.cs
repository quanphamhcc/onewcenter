using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Model-level tests for the slide service-item editor foundation (FEAT-001): the additive
/// <see cref="ServiceItem.AudioAttachments"/>/<see cref="ServiceItem.StageTemplateId"/> fields,
/// the pure <see cref="ServiceItemAudio"/> and <see cref="SlideAutoAdvanceMap"/> helpers, JSON
/// round-trip/omission behavior, legacy migration, and the SQLite mirror. All logic under test
/// lives in Core, so this test references only WorshipPlayer.Core (no linked Presenter source).
/// </summary>
public class SlideServiceItemEditorModelTests : IDisposable
{
    // Mirror the manager's _jsonOptions shape so round-trips/omission match persistence exactly.
    private static readonly JsonSerializerOptions ManagerJsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _tempFolder;

    public SlideServiceItemEditorModelTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"SlideEditorTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        // SQLite pools keep a file handle open after connections are disposed; clear them so the
        // temp .db can be deleted without an IOException (mirrors TemplateServiceTests).
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempFolder))
        {
            try { Directory.Delete(_tempFolder, true); }
            catch (IOException) { /* best-effort temp cleanup */ }
        }
    }

    // ─── (a) ServiceItemAudio.Resolve ─────────────────────────────

    [Fact]
    public void Resolve_LegacySinglePath_YieldsOneElementList()
    {
        var item = new ServiceItem { AudioAttachmentPath = "C:\\music\\intro.wav" };

        var resolved = ServiceItemAudio.Resolve(item);

        Assert.Equal(new[] { "C:\\music\\intro.wav" }, resolved);
    }

    [Fact]
    public void Resolve_PopulatedAttachments_WinsOverLegacyPath()
    {
        var item = new ServiceItem
        {
            AudioAttachmentPath = "C:\\music\\legacy.wav",
            AudioAttachments = new List<string> { "a.wav", "b.wav" }
        };

        var resolved = ServiceItemAudio.Resolve(item);

        Assert.Equal(new[] { "a.wav", "b.wav" }, resolved);
    }

    [Fact]
    public void Resolve_BothEmpty_YieldsEmptyList()
    {
        var item = new ServiceItem();

        var resolved = ServiceItemAudio.Resolve(item);

        Assert.Empty(resolved);
    }

    [Fact]
    public void Resolve_DoesNotMutateItem()
    {
        var item = new ServiceItem { AudioAttachmentPath = "legacy.wav" };

        ServiceItemAudio.Resolve(item);

        Assert.Null(item.AudioAttachments);
        Assert.Equal("legacy.wav", item.AudioAttachmentPath);
    }

    // ─── (b) JSON round-trip + WhenWritingNull omission ───────────

    [Fact]
    public void JsonRoundTrip_WithAdditiveFields_RoundTripsEqual()
    {
        var item = new ServiceItem
        {
            Id = "item-1",
            AudioAttachments = new List<string> { "a.wav", "b.wav" },
            StageTemplateId = "stage-template-7",
            AutoAdvanceSettings = new ServiceItemSettings
            {
                AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
                GoToItemId = "x"
            }
        };

        var json = JsonSerializer.Serialize(item, ManagerJsonOptions);
        var restored = JsonSerializer.Deserialize<ServiceItem>(json, ManagerJsonOptions);

        Assert.NotNull(restored);
        Assert.Equal(new[] { "a.wav", "b.wav" }, restored!.AudioAttachments);
        Assert.Equal("stage-template-7", restored.StageTemplateId);
        Assert.NotNull(restored.AutoAdvanceSettings);
        Assert.Equal(AutoAdvanceGoTo.SpecificItem, restored.AutoAdvanceSettings!.AutoAdvanceGoTo);
        Assert.Equal("x", restored.AutoAdvanceSettings.GoToItemId);
    }

    [Fact]
    public void JsonSerialize_NullAdditiveFields_OmitsKeys()
    {
        var item = new ServiceItem
        {
            AudioAttachments = null,
            StageTemplateId = null
        };

        var json = JsonSerializer.Serialize(item, ManagerJsonOptions);

        Assert.DoesNotContain("audioAttachments", json);
        Assert.DoesNotContain("stageTemplateId", json);
    }

    // ─── (c) Legacy-file load ─────────────────────────────────────

    [Fact]
    public void LegacyLoad_NoNewKeys_MigratesPathViaResolve()
    {
        const string legacyJson = """
        {
            "id": "legacy-item",
            "type": "Announcement",
            "title": "Welcome",
            "audioAttachmentPath": "C:\\music\\bed.mp3"
        }
        """;

        var item = JsonSerializer.Deserialize<ServiceItem>(legacyJson, ManagerJsonOptions);

        Assert.NotNull(item);
        Assert.Null(item!.AudioAttachments);
        Assert.Null(item.StageTemplateId);

        var resolved = ServiceItemAudio.Resolve(item);
        Assert.Equal("C:\\music\\bed.mp3", resolved[0]);
    }

    // ─── (d) SlideAutoAdvanceMap load/save table ──────────────────

    [Theory]
    [InlineData(SlideAutoAdvanceMap.AfterDisabled, AutoAdvanceAfterCondition.Manual)]
    [InlineData(SlideAutoAdvanceMap.AfterTimeDelay, AutoAdvanceAfterCondition.Timer)]
    [InlineData(SlideAutoAdvanceMap.AfterEndOfMedia, AutoAdvanceAfterCondition.AudioEnd)]
    [InlineData("something unknown", AutoAdvanceAfterCondition.Manual)]
    public void SaveAfter_MapsLabelToCondition(string afterLabel, AutoAdvanceAfterCondition expected)
    {
        var settings = new ServiceItemSettings();

        SlideAutoAdvanceMap.SaveAfter(settings, afterLabel);

        Assert.Equal(expected, settings.AutoAdvanceAfterCondition);
    }

    [Theory]
    [InlineData(AutoAdvanceAfterCondition.Manual, SlideAutoAdvanceMap.AfterDisabled)]
    [InlineData(AutoAdvanceAfterCondition.Timer, SlideAutoAdvanceMap.AfterTimeDelay)]
    [InlineData(AutoAdvanceAfterCondition.AudioEnd, SlideAutoAdvanceMap.AfterEndOfMedia)]
    [InlineData(AutoAdvanceAfterCondition.VideoEnd, SlideAutoAdvanceMap.AfterEndOfMedia)]
    public void AfterLabel_MapsConditionToLabel(AutoAdvanceAfterCondition condition, string expected)
    {
        var settings = new ServiceItemSettings { AutoAdvanceAfterCondition = condition };

        Assert.Equal(expected, SlideAutoAdvanceMap.AfterLabel(settings));
    }

    [Fact]
    public void AfterCondition_VideoEnd_LoadFoldsToEndOfMedia_ThenResavesAsAudioEnd()
    {
        // Load: a VideoEnd item surfaces as "End of Media" in the dropdown.
        var loaded = new ServiceItemSettings { AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.VideoEnd };
        var label = SlideAutoAdvanceMap.AfterLabel(loaded);
        Assert.Equal(SlideAutoAdvanceMap.AfterEndOfMedia, label);

        // Save: re-saving that "End of Media" label normalizes to AudioEnd.
        var saved = new ServiceItemSettings();
        SlideAutoAdvanceMap.SaveAfter(saved, label);
        Assert.Equal(AutoAdvanceAfterCondition.AudioEnd, saved.AutoAdvanceAfterCondition);
    }

    [Theory]
    [InlineData(SlideAutoAdvanceMap.GoToDoNothing, AutoAdvanceGoTo.NextSlide)]
    [InlineData(SlideAutoAdvanceMap.GoToNextItem, AutoAdvanceGoTo.NextItem)]
    [InlineData(SlideAutoAdvanceMap.GoToLoop, AutoAdvanceGoTo.Loop)]
    public void SaveWhenComplete_NonSpecific_MapsAndClearsItemId(string goToLabel, AutoAdvanceGoTo expected)
    {
        var settings = new ServiceItemSettings { GoToItemId = "stale" };

        SlideAutoAdvanceMap.SaveWhenComplete(settings, goToLabel, "ignored-target");

        Assert.Equal(expected, settings.AutoAdvanceGoTo);
        Assert.Null(settings.GoToItemId);
    }

    [Fact]
    public void SaveWhenComplete_SpecificItem_SetsItemId()
    {
        var settings = new ServiceItemSettings();

        SlideAutoAdvanceMap.SaveWhenComplete(settings, SlideAutoAdvanceMap.GoToSpecificItem, "target-42");

        Assert.Equal(AutoAdvanceGoTo.SpecificItem, settings.AutoAdvanceGoTo);
        Assert.Equal("target-42", settings.GoToItemId);
    }

    [Theory]
    [InlineData(AutoAdvanceGoTo.NextSlide, SlideAutoAdvanceMap.GoToDoNothing)]
    [InlineData(AutoAdvanceGoTo.NextItem, SlideAutoAdvanceMap.GoToNextItem)]
    [InlineData(AutoAdvanceGoTo.SpecificItem, SlideAutoAdvanceMap.GoToSpecificItem)]
    [InlineData(AutoAdvanceGoTo.Loop, SlideAutoAdvanceMap.GoToLoop)]
    public void GoToLabel_MapsGoToToLabel(AutoAdvanceGoTo goTo, string expected)
    {
        var settings = new ServiceItemSettings { AutoAdvanceGoTo = goTo };

        Assert.Equal(expected, SlideAutoAdvanceMap.GoToLabel(settings));
    }

    [Theory]
    [InlineData(SlideAutoAdvanceMap.AfterDisabled, false)]
    [InlineData(SlideAutoAdvanceMap.AfterTimeDelay, true)]
    [InlineData(SlideAutoAdvanceMap.AfterEndOfMedia, true)]
    public void WhenCompleteEnabled_FollowsAfterLabel(string afterLabel, bool expected)
    {
        Assert.Equal(expected, SlideAutoAdvanceMap.WhenCompleteEnabled(afterLabel));
    }

    // ─── (e) SQLite mirror ────────────────────────────────────────

    [Fact]
    public async Task SqliteMirror_PersistsAdditiveFieldsInDataJson()
    {
        var serviceOrdersFolder = Path.Combine(_tempFolder, "ServiceOrders");
        Directory.CreateDirectory(serviceOrdersFolder);
        var dbPath = Path.Combine(_tempFolder, "app.db");

        var manager = new ServiceOrderManager(serviceOrdersFolder, dbPath);

        var order = manager.CreateNew("Mirror Test", "2026-01-11");
        var item = new ServiceItem
        {
            Id = "mirror-item",
            Type = ServiceItemType.Slides,
            Title = "Mirror",
            AudioAttachments = new List<string> { "one.wav", "two.wav" },
            StageTemplateId = "stage-99",
            AutoAdvanceSettings = new ServiceItemSettings
            {
                AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
                GoToItemId = "target-x"
            }
        };
        manager.AddItem(item);

        var filePath = manager.GetFilePathFor(order);
        await manager.SaveAsync(filePath);

        // Read DataJson straight out of the app.db ServiceOrders row.
        string dataJson;
        using (var connection = new SqliteConnection($"Data Source={dbPath}"))
        {
            await connection.OpenAsync();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "SELECT DataJson FROM ServiceOrders LIMIT 1";
            dataJson = (string)(await cmd.ExecuteScalarAsync())!;
        }

        Assert.Contains("audioAttachments", dataJson);
        Assert.Contains("stageTemplateId", dataJson);

        var mirrored = JsonSerializer.Deserialize<ServiceOrder>(dataJson, ManagerJsonOptions);
        Assert.NotNull(mirrored);
        var mirroredItem = Assert.Single(mirrored!.Items);
        Assert.Equal(new[] { "one.wav", "two.wav" }, mirroredItem.AudioAttachments);
        Assert.Equal("stage-99", mirroredItem.StageTemplateId);
        Assert.Equal(AutoAdvanceGoTo.SpecificItem, mirroredItem.AutoAdvanceSettings!.AutoAdvanceGoTo);
        Assert.Equal("target-x", mirroredItem.AutoAdvanceSettings.GoToItemId);
    }

    // ─── (f) FEAT-003: end-to-end host save path (JSON file round-trip) ──────

    /// <summary>
    /// FEAT-003 integration round-trip: a Slides item carrying all additive fields plus an
    /// AutoAdvanceSettings with AutoAdvanceGoTo=SpecificItem + GoToItemId is saved through
    /// ServiceOrderManager (the exact path the OperatorWindow host runs on Save-and-close), then
    /// reloaded from the authoritative JSON file by a FRESH manager — the "restart" the UI depends
    /// on. This verifies the host save path without any WPF dependency.
    /// </summary>
    [Fact]
    public async Task HostSavePath_SlidesItem_RoundTripsThroughJsonFile()
    {
        var serviceOrdersFolder = Path.Combine(_tempFolder, "ServiceOrders");
        Directory.CreateDirectory(serviceOrdersFolder);
        var dbPath = Path.Combine(_tempFolder, "app.db");

        var manager = new ServiceOrderManager(serviceOrdersFolder, dbPath);
        var order = manager.CreateNew("Host Save Test", "2026-02-14");
        var item = new ServiceItem
        {
            Id = "slides-item",
            Type = ServiceItemType.Slides,
            Title = "Linked Slides",
            SlidePresentationId = "pres-1",
            BackgroundType = BackgroundType.Video,
            MediaPath = "C:\\media\\loop.mp4",
            MediaType = MediaItemType.Video,
            TransitionTimeMs = 1200,
            AudioAttachments = new List<string> { "one.wav", "two.wav" },
            AudioAttachmentPath = "one.wav",
            StageTemplateId = "stage-42",
            AutoAdvanceSettings = new ServiceItemSettings
            {
                AutoAdvanceAfterCondition = AutoAdvanceAfterCondition.Timer,
                AutoAdvanceGoTo = AutoAdvanceGoTo.SpecificItem,
                GoToItemId = "target-item",
                TransitionFadeTimeMs = 1200
            }
        };
        manager.AddItem(item);

        var filePath = manager.GetFilePathFor(order);
        await manager.SaveAsync(filePath);

        // Reload from the authoritative JSON file with a fresh manager (the UI "restart").
        var reloadManager = new ServiceOrderManager(serviceOrdersFolder, dbPath);
        var reloaded = await reloadManager.LoadAsync(filePath);

        Assert.NotNull(reloaded);
        var reloadedItem = Assert.Single(reloaded!.Items);
        Assert.Equal(ServiceItemType.Slides, reloadedItem.Type);
        Assert.Equal("pres-1", reloadedItem.SlidePresentationId);
        Assert.Equal(BackgroundType.Video, reloadedItem.BackgroundType);
        Assert.Equal("C:\\media\\loop.mp4", reloadedItem.MediaPath);
        Assert.Equal(MediaItemType.Video, reloadedItem.MediaType);
        Assert.Equal(1200, reloadedItem.TransitionTimeMs);
        Assert.Equal(new[] { "one.wav", "two.wav" }, reloadedItem.AudioAttachments);
        Assert.Equal("one.wav", reloadedItem.AudioAttachmentPath);
        Assert.Equal("stage-42", reloadedItem.StageTemplateId);
        Assert.NotNull(reloadedItem.AutoAdvanceSettings);
        Assert.Equal(AutoAdvanceAfterCondition.Timer, reloadedItem.AutoAdvanceSettings!.AutoAdvanceAfterCondition);
        Assert.Equal(AutoAdvanceGoTo.SpecificItem, reloadedItem.AutoAdvanceSettings.AutoAdvanceGoTo);
        Assert.Equal("target-item", reloadedItem.AutoAdvanceSettings.GoToItemId);
        Assert.Equal(1200, reloadedItem.AutoAdvanceSettings.TransitionFadeTimeMs);
    }

    // ─── (g) FEAT-003: the real service-order JSON files still load ──────────

    /// <summary>
    /// FEAT-003 regression guard: every real/demo service-order JSON shipped under the worktree's
    /// ServiceOrders folder must still deserialize via ServiceOrderManager.LoadAsync after the
    /// additive model fields were introduced. Each file copies into a temp ServiceOrders folder so
    /// LoadAsync's SQLite mirror upsert hits a throwaway app.db (no runtime data is touched). If the
    /// folder is not found (CI without the sample files), the test no-ops rather than failing.
    /// </summary>
    [Fact]
    public async Task RealServiceOrderFiles_StillDeserializeWithoutError()
    {
        var sourceFolder = FindRepoServiceOrdersFolder();
        if (sourceFolder == null) return; // sample files not present in this checkout — nothing to assert.

        var realFiles = Directory.GetFiles(sourceFolder, "*.json");
        if (realFiles.Length == 0) return;

        var serviceOrdersFolder = Path.Combine(_tempFolder, "ServiceOrders");
        Directory.CreateDirectory(serviceOrdersFolder);
        var dbPath = Path.Combine(_tempFolder, "app.db");
        var manager = new ServiceOrderManager(serviceOrdersFolder, dbPath);

        foreach (var sourceFile in realFiles)
        {
            var copy = Path.Combine(serviceOrdersFolder, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, copy, overwrite: true);

            var loaded = await manager.LoadAsync(copy);

            Assert.NotNull(loaded);
            Assert.NotNull(loaded!.Items);
        }
    }

    /// <summary>
    /// Walk up from the test assembly's base directory to the worktree root and return its
    /// ServiceOrders folder, or null if not found.
    /// </summary>
    private static string? FindRepoServiceOrdersFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "ServiceOrders");
            if (Directory.Exists(candidate) && Directory.GetFiles(candidate, "*.json").Length > 0)
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
