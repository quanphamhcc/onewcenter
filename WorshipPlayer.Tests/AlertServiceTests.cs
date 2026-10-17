using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Presenter.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for AlertService (SQLite-backed, app.db).
/// Tests template CRUD persistence, the in-memory alert display state/events (unchanged), and
/// the one-time alert-templates.json import. Each test injects a temp app.db and clears SQLite
/// connection pools in teardown so the temp .db file can be deleted on Windows (mirrors
/// SongUsageServiceTests discipline).
/// </summary>
public class AlertServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _dbPath;
    private readonly AlertService _service;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public AlertServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"AlertTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _dbPath = Path.Combine(_tempFolder, "app.db");
        _service = new AlertService(_dbPath);
    }

    public void Dispose()
    {
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
    public async Task SaveTemplateAsync_AssignsIdWhenEmpty()
    {
        var template = new AlertTemplate { Id = "", Name = "Welcome", Text = "Service starts soon" };

        var saved = await _service.SaveTemplateAsync(template);

        Assert.False(string.IsNullOrEmpty(saved.Id));
    }

    [Fact]
    public async Task SaveTemplateAsync_PreservesExistingId()
    {
        var template = new AlertTemplate { Id = "fixed-alert-id", Name = "Nursery" };

        var saved = await _service.SaveTemplateAsync(template);

        Assert.Equal("fixed-alert-id", saved.Id);
    }

    [Fact]
    public async Task GetTemplatesAsync_RoundTripsAllStylingFields()
    {
        var template = new AlertTemplate
        {
            Id = "styled-1",
            Name = "Styled Alert",
            Text = "Please silence phones",
            FontSize = 72,
            FontFamily = "Arial",
            FontColor = "#FF0000",
            HasShadow = false,
            ShadowColor = "#111111",
            HasOutline = true,
            OutlineColor = "#00FF00",
            OutlineWidth = 2.5,
            ShowOnStageDisplay = false,
            OrganizationId = "org-42"
        };
        await _service.SaveTemplateAsync(template);

        var all = await _service.GetTemplatesAsync();
        var loaded = Assert.Single(all);

        Assert.Equal("Styled Alert", loaded.Name);
        Assert.Equal("Please silence phones", loaded.Text);
        Assert.Equal(72, loaded.FontSize);
        Assert.Equal("Arial", loaded.FontFamily);
        Assert.Equal("#FF0000", loaded.FontColor);
        Assert.False(loaded.HasShadow);
        Assert.Equal("#111111", loaded.ShadowColor);
        Assert.True(loaded.HasOutline);
        Assert.Equal("#00FF00", loaded.OutlineColor);
        Assert.Equal(2.5, loaded.OutlineWidth);
        Assert.False(loaded.ShowOnStageDisplay);
        Assert.Equal("org-42", loaded.OrganizationId);
    }

    [Fact]
    public async Task SaveTemplateAsync_ExistingId_UpdatesInPlaceNoDuplicate()
    {
        var template = new AlertTemplate { Id = "upsert-1", Name = "Original" };
        await _service.SaveTemplateAsync(template);

        template.Name = "Updated";
        template.Text = "New text";
        await _service.SaveTemplateAsync(template);

        var all = await _service.GetTemplatesAsync();
        var single = Assert.Single(all);
        Assert.Equal("Updated", single.Name);
        Assert.Equal("New text", single.Text);
    }

    [Fact]
    public async Task DeleteTemplateAsync_RemovesTemplate()
    {
        var template = new AlertTemplate { Id = "del-1", Name = "To Delete" };
        await _service.SaveTemplateAsync(template);

        await _service.DeleteTemplateAsync("del-1");

        var all = await _service.GetTemplatesAsync();
        Assert.Empty(all);
    }

    [Fact]
    public async Task DeleteTemplateAsync_AbsentId_IsNoOp()
    {
        var template = new AlertTemplate { Id = "keep-1", Name = "Keep" };
        await _service.SaveTemplateAsync(template);

        // Deleting a non-existent id must not throw and must not remove the existing row.
        await _service.DeleteTemplateAsync("does-not-exist");

        var all = await _service.GetTemplatesAsync();
        Assert.Single(all);
    }

    [Fact]
    public async Task Templates_PersistAcrossServiceInstances()
    {
        await _service.SaveTemplateAsync(new AlertTemplate { Id = "persist-1", Name = "Persisted" });

        Assert.True(File.Exists(_dbPath));

        var reopened = new AlertService(_dbPath);
        var all = await reopened.GetTemplatesAsync();
        var single = Assert.Single(all);
        Assert.Equal("Persisted", single.Name);
    }

    // ─── In-memory display state / events (unchanged) ──────────────────────

    [Fact]
    public async Task ShowAlertAsync_SetsActiveStateAndRaisesEvent()
    {
        var template = new AlertTemplate { Name = "Live Alert" };
        AlertTemplate? raised = null;
        _service.AlertShown += (_, t) => raised = t;

        await _service.ShowAlertAsync(template);

        Assert.True(_service.IsAlertActive);
        Assert.Same(template, _service.CurrentAlert);
        Assert.Same(template, raised);
    }

    [Fact]
    public async Task DismissAlertAsync_ClearsStateAndRaisesEvent()
    {
        await _service.ShowAlertAsync(new AlertTemplate { Name = "Live Alert" });

        var dismissed = false;
        _service.AlertDismissed += (_, _) => dismissed = true;

        await _service.DismissAlertAsync();

        Assert.False(_service.IsAlertActive);
        Assert.Null(_service.CurrentAlert);
        Assert.True(dismissed);
    }

    // ─── One-time legacy import tests ──────────────────────────────────────

    [Fact]
    public async Task Import_LegacyJson_ImportsOnceWithoutDuplicating()
    {
        var legacyJsonPath = Path.Combine(_tempFolder, "alert-templates.json");
        var legacy = new List<AlertTemplate>
        {
            new() { Id = "legacy-alert-1", Name = "Legacy A", Text = "Alpha" },
            new() { Id = "legacy-alert-2", Name = "Legacy B", Text = "Beta" }
        };
        await File.WriteAllTextAsync(legacyJsonPath, JsonSerializer.Serialize(legacy, JsonOptions));

        var first = new AlertService(_dbPath);
        var afterImport = await first.GetTemplatesAsync();
        Assert.Equal(2, afterImport.Count);
        Assert.Contains(afterImport, t => t.Id == "legacy-alert-1" && t.Name == "Legacy A");

        var second = new AlertService(_dbPath);
        var afterSecond = await second.GetTemplatesAsync();
        Assert.Equal(2, afterSecond.Count);
    }

    [Fact]
    public async Task Import_MalformedLegacyJson_DoesNotThrowAndYieldsEmptyStore()
    {
        var legacyJsonPath = Path.Combine(_tempFolder, "alert-templates.json");
        await File.WriteAllTextAsync(legacyJsonPath, "{ this is not valid json ]");

        var service = new AlertService(_dbPath);

        var all = await service.GetTemplatesAsync();
        Assert.Empty(all);
    }
}
