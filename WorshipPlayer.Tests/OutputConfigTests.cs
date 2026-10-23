using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using Xunit;

namespace WorshipPlayer.Tests;

/// <summary>
/// Đợt 2 T15 / R8 — migration danh sách outputs + resolver template theo output.
/// Migration: settings cũ chỉ có OutputDisplay/StageDisplay → danh sách output tương đương
/// (1 Audience "Main Audience" + 1 Stage nếu có); các trường cũ giữ lại.
/// Resolver: template hiệu dụng = override của output → template của item → template mặc định;
/// output Stage dùng StageTemplateId của item (override foreground không áp cho Stage).
/// Backward-compat: settings.json hiện hữu ở gốc repo nạp được bằng model mới.
/// </summary>
public class OutputConfigTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _dbPath;

    public OutputConfigTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"OutputConfigTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _dbPath = Path.Combine(_tempFolder, "app.db");
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_tempFolder))
        {
            try { Directory.Delete(_tempFolder, true); }
            catch (IOException) { /* best-effort cleanup */ }
        }
    }

    // ─── Migration ────────────────────────────────────────────────────────────

    [Fact]
    public void EnsureOutputs_BothLegacyIndices_ProducesAudienceAndStage()
    {
        var settings = new PresenterSettings { OutputDisplay = 1, StageDisplay = 2 };

        var outputs = OutputMigration.EnsureOutputs(settings);

        Assert.Same(settings.Outputs, outputs);
        Assert.Equal(2, outputs.Count);

        var audience = outputs[0];
        Assert.Equal(OutputMigration.MainAudienceOutputId, audience.Id);
        Assert.Equal("Main Audience", audience.Name);
        Assert.Equal(1, audience.DisplayIndex);
        Assert.Equal(ScreenPurpose.Audience, audience.Purpose);
        Assert.Null(audience.ForegroundTemplateIdOverride);

        var stage = outputs[1];
        Assert.Equal(OutputMigration.StageOutputId, stage.Id);
        Assert.Equal("Stage Display", stage.Name);
        Assert.Equal(2, stage.DisplayIndex);
        Assert.Equal(ScreenPurpose.Stage, stage.Purpose);
    }

    [Fact]
    public void EnsureOutputs_OnlyAudience_MigratesSingleOutput()
    {
        var settings = new PresenterSettings { OutputDisplay = 0, StageDisplay = -1 };

        var outputs = OutputMigration.EnsureOutputs(settings);

        Assert.Single(outputs);
        Assert.Equal(ScreenPurpose.Audience, outputs[0].Purpose);
        Assert.Equal(0, outputs[0].DisplayIndex);
    }

    [Fact]
    public void EnsureOutputs_NothingConfigured_YieldsEmptyList()
    {
        var settings = new PresenterSettings { OutputDisplay = -1, StageDisplay = -1 };

        var outputs = OutputMigration.EnsureOutputs(settings);

        Assert.NotNull(outputs);
        Assert.Empty(outputs);
    }

    [Fact]
    public void EnsureOutputs_PresentList_IsKeptUntouched_EvenWhenEmpty()
    {
        var kept = new List<OutputConfig>
        {
            new() { Id = "custom", Name = "Livestream", DisplayIndex = 3, Purpose = ScreenPurpose.Audience }
        };
        var settings = new PresenterSettings { OutputDisplay = 9, StageDisplay = 9, Outputs = kept };

        var outputs = OutputMigration.EnsureOutputs(settings);

        Assert.Same(kept, outputs);
        Assert.Single(outputs);
        Assert.Equal("Livestream", outputs[0].Name);

        // Empty list is also the user's own state — not re-migrated.
        var empty = new PresenterSettings { OutputDisplay = 1, Outputs = new List<OutputConfig>() };
        Assert.Empty(OutputMigration.EnsureOutputs(empty));
    }

    [Fact]
    public void EnsureOutputs_IsIdempotent()
    {
        var settings = new PresenterSettings { OutputDisplay = 1, StageDisplay = 2 };

        var first = OutputMigration.EnsureOutputs(settings);
        var second = OutputMigration.EnsureOutputs(settings);

        Assert.Same(first, second);
        Assert.Equal(2, second.Count);
    }

    // ─── Resolver ─────────────────────────────────────────────────────────────

    [Fact]
    public void Resolve_AudienceOverride_WinsOverItemAndDefault()
    {
        var output = new OutputConfig
        {
            Purpose = ScreenPurpose.Audience,
            ForegroundTemplateIdOverride = "builtin-lower-third"
        };

        var result = OutputTemplateResolver.Resolve(output, "section-template", "builtin-default-centered");

        Assert.Equal("builtin-lower-third", result);
    }

    [Fact]
    public void Resolve_NoOverride_FallsThroughToItemTemplate()
    {
        var output = new OutputConfig { Purpose = ScreenPurpose.Audience };

        var result = OutputTemplateResolver.Resolve(output, "section-template", "builtin-default-centered");

        Assert.Equal("section-template", result);
    }

    [Fact]
    public void Resolve_NoOverrideNoItem_FallsThroughToDefault()
    {
        var output = new OutputConfig { Purpose = ScreenPurpose.Audience };

        var result = OutputTemplateResolver.Resolve(output, null, "builtin-default-centered");

        Assert.Equal("builtin-default-centered", result);
    }

    [Fact]
    public void Resolve_AllUnset_ReturnsNull()
    {
        var output = new OutputConfig { Purpose = ScreenPurpose.Audience };

        Assert.Null(OutputTemplateResolver.Resolve(output, null, null));
    }

    [Fact]
    public void Resolve_WhitespaceOverride_IsTreatedAsUnset()
    {
        var output = new OutputConfig
        {
            Purpose = ScreenPurpose.Audience,
            ForegroundTemplateIdOverride = "   "
        };

        var result = OutputTemplateResolver.Resolve(output, "section-template", "default");

        Assert.Equal("section-template", result);
    }

    [Fact]
    public void Resolve_StageOutput_UsesStageTemplateId_IgnoresForegroundOverride()
    {
        // Pre-T15 stage behavior preserved: the foreground override never applies to a
        // stage output; the item's StageTemplateId wins when set.
        var output = new OutputConfig
        {
            Purpose = ScreenPurpose.Stage,
            ForegroundTemplateIdOverride = "builtin-lower-third"
        };

        var result = OutputTemplateResolver.Resolve(output, "stage-template-of-item", "default-stage");

        Assert.Equal("stage-template-of-item", result);
    }

    [Fact]
    public void Resolve_StageOutput_NoStageTemplate_FallsThroughToDefault()
    {
        var output = new OutputConfig { Purpose = ScreenPurpose.Stage };

        var result = OutputTemplateResolver.Resolve(output, null, "default-stage");

        Assert.Equal("default-stage", result);
    }

    [Fact]
    public void Resolve_NullOutput_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OutputTemplateResolver.Resolve(null!, "x", "y"));
    }

    // ─── Backward compat ──────────────────────────────────────────────────────

    [Fact]
    public async Task LegacyRepoSettingsJson_LoadsWithNewModel_AndMigratesToEmptyList()
    {
        // settings.json hiện hữu ở gốc repo (AppSettings payload, không có key display nào)
        // phải nạp được bằng model PresenterSettings mới — thay đổi model thuần cộng thêm.
        var asmDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!;
        // Assembly nằm ở <repo>/WorshipPlayer.Tests/bin/Debug/net8.0 → 4 cấp lên là gốc repo.
        var repoRoot = Path.GetFullPath(Path.Combine(asmDir, "..", "..", "..", ".."));
        var settingsPath = Path.Combine(repoRoot, "settings.json");

        Assert.True(File.Exists(settingsPath), $"settings.json not found at {settingsPath}");

        var json = await File.ReadAllTextAsync(settingsPath);
        var settings = JsonSerializer.Deserialize<PresenterSettings>(json);

        Assert.NotNull(settings);
        // File không có key outputDisplay/stageDisplay → mặc định -1 → migration cho list rỗng.
        var outputs = OutputMigration.EnsureOutputs(settings);
        Assert.Empty(outputs);
        Assert.Equal(-1, settings.OutputDisplay);
        Assert.Equal(-1, settings.StageDisplay);
    }

    [Fact]
    public async Task LoadAsync_LegacyRow_MigratesOutputs_OnRealSqlite()
    {
        // Seed một row presenter-settings cũ (chỉ có OutputDisplay/StageDisplay, không có
        // key "outputs") vào app.db thật, rồi LoadAsync phải tự migrate danh sách outputs.
        var legacyJson = "{\"outputDisplay\":2,\"stageDisplay\":3}";
        var dbDir = Path.GetDirectoryName(_dbPath)!;
        Directory.CreateDirectory(dbDir);

        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            await connection.OpenAsync();
            using var schema = connection.CreateCommand();
            schema.CommandText = "CREATE TABLE IF NOT EXISTS PresenterSettings (Id INTEGER PRIMARY KEY CHECK (Id = 1), DataJson TEXT NOT NULL);";
            await schema.ExecuteNonQueryAsync();

            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO PresenterSettings (Id, DataJson) VALUES (1, @j)";
            insert.Parameters.AddWithValue("@j", legacyJson);
            await insert.ExecuteNonQueryAsync();
        }

        var service = new PresenterSettingsService(_dbPath);
        await service.LoadAsync();

        var outputs = service.Settings.Outputs;
        Assert.NotNull(outputs);
        Assert.Equal(2, outputs.Count);
        Assert.Equal("Main Audience", outputs[0].Name);
        Assert.Equal(2, outputs[0].DisplayIndex);
        Assert.Equal(ScreenPurpose.Audience, outputs[0].Purpose);
        Assert.Equal("Stage Display", outputs[1].Name);
        Assert.Equal(3, outputs[1].DisplayIndex);
        Assert.Equal(ScreenPurpose.Stage, outputs[1].Purpose);

        // Trường cũ còn nguyên (tương thích ngược cho DisplayManager tới khi T16 nối lại).
        Assert.Equal(2, service.Settings.OutputDisplay);
        Assert.Equal(3, service.Settings.StageDisplay);
    }
}
