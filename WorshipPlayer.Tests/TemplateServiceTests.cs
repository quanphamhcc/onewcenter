using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Presenter.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for TemplateService (SQLite-backed, app.db).
/// Tests built-in templates, CRUD operations, protection of built-in templates, and the
/// one-time legacy Templates-folder import. Each test injects a temp app.db and clears SQLite
/// connection pools in teardown so the temp .db file can be deleted on Windows (mirrors
/// SongUsageServiceTests discipline).
/// </summary>
public class TemplateServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _dbPath;
    private readonly TemplateService _service;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public TemplateServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"TemplateTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _dbPath = Path.Combine(_tempFolder, "app.db");
        _service = new TemplateService(_dbPath);
    }

    public void Dispose()
    {
        // SQLite pools keep a file handle open after connections are disposed; clear the
        // pools first so the temp .db can be deleted without an IOException.
        SqliteConnection.ClearAllPools();

        if (Directory.Exists(_tempFolder))
        {
            try
            {
                Directory.Delete(_tempFolder, true);
            }
            catch (IOException)
            {
                // Best-effort cleanup of a temp folder; ignore a transient OS lock.
            }
        }
    }

    [Fact]
    public async Task GetAllAsync_IncludesBuiltInTemplates()
    {
        var templates = await _service.GetAllAsync();

        Assert.True(templates.Count >= 3);
    }

    [Fact]
    public async Task GetAllAsync_HasDefaultCentered()
    {
        var templates = await _service.GetAllAsync();

        Assert.Contains(templates, t => t.Name == "Default Centered");
    }

    [Fact]
    public async Task GetAllAsync_HasLowerThird()
    {
        var templates = await _service.GetAllAsync();

        Assert.Contains(templates, t => t.Name == "Lower Third");
    }

    [Fact]
    public async Task GetAllAsync_HasTrendyLine()
    {
        var templates = await _service.GetAllAsync();

        Assert.Contains(templates, t => t.Name == "Trendy Line");
    }

    [Fact]
    public async Task GetBuiltInTemplatesAsync_ReturnsThreeTemplates()
    {
        var builtIns = await _service.GetBuiltInTemplatesAsync();

        Assert.Equal(3, builtIns.Count);
        Assert.All(builtIns, t => Assert.True(t.IsBuiltIn));
    }

    [Fact]
    public async Task CreateAsync_CustomTemplate_AddsToList()
    {
        var custom = new SlideTemplate
        {
            Name = "My Custom Template",
            FontSize = 48,
            FontColor = "#FF0000"
        };

        var created = await _service.CreateAsync(custom);

        Assert.NotNull(created.Id);
        Assert.False(created.IsBuiltIn);

        var all = await _service.GetAllAsync();
        Assert.Contains(all, t => t.Name == "My Custom Template");
    }

    [Fact]
    public async Task CreateAsync_PersistsToDatabase()
    {
        var custom = new SlideTemplate
        {
            Name = "Persisted Template",
            FontSize = 72
        };

        await _service.CreateAsync(custom);

        // DB-backed persistence: a fresh service against the same app.db reads it back
        // (replaces the old "a *.json file was written" check).
        Assert.True(File.Exists(_dbPath));

        var reopened = new TemplateService(_dbPath);
        var all = await reopened.GetAllAsync();
        Assert.Contains(all, t => t.Name == "Persisted Template");
    }

    [Fact]
    public async Task DeleteAsync_CustomTemplate_ReturnsTrue()
    {
        var custom = new SlideTemplate { Name = "To Delete" };
        var created = await _service.CreateAsync(custom);

        var deleted = await _service.DeleteAsync(created.Id);

        Assert.True(deleted);

        var all = await _service.GetAllAsync();
        Assert.DoesNotContain(all, t => t.Id == created.Id);
    }

    [Fact]
    public async Task DeleteAsync_BuiltInTemplate_ReturnsFalse()
    {
        var builtIns = await _service.GetBuiltInTemplatesAsync();
        var defaultCentered = builtIns.First(t => t.Name == "Default Centered");

        var deleted = await _service.DeleteAsync(defaultCentered.Id);

        Assert.False(deleted);

        // Verify it still exists
        var all = await _service.GetAllAsync();
        Assert.Contains(all, t => t.Name == "Default Centered");
    }

    [Fact]
    public async Task DeleteAsync_CannotDeleteLowerThird()
    {
        var builtIns = await _service.GetBuiltInTemplatesAsync();
        var lowerThird = builtIns.First(t => t.Name == "Lower Third");

        var deleted = await _service.DeleteAsync(lowerThird.Id);

        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteAsync_CannotDeleteTrendyLine()
    {
        var builtIns = await _service.GetBuiltInTemplatesAsync();
        var trendyLine = builtIns.First(t => t.Name == "Trendy Line");

        var deleted = await _service.DeleteAsync(trendyLine.Id);

        Assert.False(deleted);
    }

    [Fact]
    public async Task DeleteAsync_NonExistentId_ReturnsFalse()
    {
        var deleted = await _service.DeleteAsync("non-existent-id");
        Assert.False(deleted);
    }

    [Fact]
    public async Task UpdateAsync_CustomTemplate_UpdatesValues()
    {
        var custom = new SlideTemplate { Name = "Original Name", FontSize = 40 };
        var created = await _service.CreateAsync(custom);

        created.Name = "Updated Name";
        created.FontSize = 80;
        await _service.UpdateAsync(created);

        var updated = await _service.GetByIdAsync(created.Id);
        Assert.NotNull(updated);
        Assert.Equal("Updated Name", updated!.Name);
        Assert.Equal(80, updated.FontSize);
    }

    [Fact]
    public async Task UpdateAsync_NonExistent_ThrowsException()
    {
        var template = new SlideTemplate { Id = "non-existent", Name = "Test" };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UpdateAsync(template));
    }

    [Fact]
    public async Task GetByIdAsync_BuiltIn_ReturnsTemplate()
    {
        var builtIns = await _service.GetBuiltInTemplatesAsync();
        var id = builtIns[0].Id;

        var template = await _service.GetByIdAsync(id);

        Assert.NotNull(template);
        Assert.True(template!.IsBuiltIn);
    }

    [Fact]
    public async Task GetByIdAsync_NonExistent_ReturnsNull()
    {
        var template = await _service.GetByIdAsync("does-not-exist");
        Assert.Null(template);
    }

    [Fact]
    public async Task GetByCategoryAsync_FiltersCorrectly()
    {
        var custom = new SlideTemplate
        {
            Name = "Scripture Template",
            TemplateCategory = TemplateCategory.Scripture
        };
        await _service.CreateAsync(custom);

        var results = await _service.GetByCategoryAsync(TemplateCategory.Scripture);

        Assert.Contains(results, t => t.Name == "Scripture Template");
    }

    [Fact]
    public async Task DuplicateAsync_CreatesNewCopy()
    {
        var builtIns = await _service.GetBuiltInTemplatesAsync();
        var source = builtIns[0];

        var duplicate = await _service.DuplicateAsync(source.Id, "My Copy");

        Assert.NotEqual(source.Id, duplicate.Id);
        Assert.Equal("My Copy", duplicate.Name);
        Assert.False(duplicate.IsBuiltIn);
        Assert.Equal(source.FontSize, duplicate.FontSize);
    }

    [Fact]
    public void GetBuiltInTemplates_Static_ReturnsThree()
    {
        var builtIns = TemplateService.GetBuiltInTemplates();

        Assert.Equal(3, builtIns.Count);
        Assert.Contains(builtIns, t => t.Name == "Default Centered");
        Assert.Contains(builtIns, t => t.Name == "Lower Third");
        Assert.Contains(builtIns, t => t.Name == "Trendy Line");
    }

    [Fact]
    public void GetBuiltInTemplates_DefaultCentered_HasCorrectProperties()
    {
        var builtIns = TemplateService.GetBuiltInTemplates();
        var dc = builtIns.First(t => t.Name == "Default Centered");

        Assert.Equal("Segoe UI", dc.FontFamily);
        Assert.Equal(60, dc.FontSize);
        Assert.Equal(TextAlignment.Center, dc.HorizontalAlignment);
        Assert.Equal(VerticalAlignment.Center, dc.VerticalAlignment);
        Assert.True(dc.HasShadow);
    }

    // ─── One-time legacy import tests ──────────────────────────────────────

    [Fact]
    public async Task Import_LegacyTemplateFolder_ImportsOnceWithoutDuplicating()
    {
        // Write a legacy Templates/*.json custom template beside the (not-yet-created) app.db.
        var templatesFolder = Path.Combine(_tempFolder, "Templates");
        Directory.CreateDirectory(templatesFolder);
        var legacy = new SlideTemplate
        {
            Id = "legacy-custom-1",
            Name = "Legacy Custom",
            FontSize = 99,
            TemplateCategory = TemplateCategory.Announcements
        };
        await File.WriteAllTextAsync(
            Path.Combine(templatesFolder, "legacy-custom.json"),
            JsonSerializer.Serialize(legacy, JsonOptions));

        // First service imports the legacy template (3 built-ins + 1 custom).
        var first = new TemplateService(_dbPath);
        var afterImport = await first.GetAllAsync();
        Assert.Equal(4, afterImport.Count);
        var imported = afterImport.Single(t => t.Id == "legacy-custom-1");
        Assert.Equal("Legacy Custom", imported.Name);
        Assert.Equal(99, imported.FontSize);
        Assert.False(imported.IsBuiltIn);

        // A second service against the same app.db must NOT re-import (no duplicates).
        var second = new TemplateService(_dbPath);
        var afterSecond = await second.GetAllAsync();
        Assert.Equal(4, afterSecond.Count);
    }

    [Fact]
    public async Task GetAllAsync_EmptyStore_ReturnsExactlyThreeBuiltInsNoCustom()
    {
        var all = await _service.GetAllAsync();

        Assert.Equal(3, all.Count);
        Assert.All(all, t => Assert.True(t.IsBuiltIn));
    }

    [Fact]
    public async Task Import_LegacyFileWithBuiltInId_IsNotImportedAsDuplicate()
    {
        // A legacy file whose id matches a built-in must NOT create a duplicate row.
        var templatesFolder = Path.Combine(_tempFolder, "Templates");
        Directory.CreateDirectory(templatesFolder);
        var masqueradingBuiltIn = new SlideTemplate
        {
            Id = "builtin-default-centered",
            Name = "Default Centered",
            FontSize = 123
        };
        await File.WriteAllTextAsync(
            Path.Combine(templatesFolder, "default-centered.json"),
            JsonSerializer.Serialize(masqueradingBuiltIn, JsonOptions));

        var all = await _service.GetAllAsync();

        // Exactly 3 built-ins, no extra row, and the code-seeded built-in wins (FontSize 60).
        Assert.Equal(3, all.Count);
        var dc = all.Single(t => t.Id == "builtin-default-centered");
        Assert.Equal(60, dc.FontSize);
        Assert.True(dc.IsBuiltIn);
    }

    [Fact]
    public async Task Import_MalformedLegacyFile_DoesNotThrowAndYieldsOnlyBuiltIns()
    {
        var templatesFolder = Path.Combine(_tempFolder, "Templates");
        Directory.CreateDirectory(templatesFolder);
        await File.WriteAllTextAsync(
            Path.Combine(templatesFolder, "broken.json"),
            "{ this is not valid json ]");

        var all = await _service.GetAllAsync();

        Assert.Equal(3, all.Count);
        Assert.All(all, t => Assert.True(t.IsBuiltIn));
    }
}
