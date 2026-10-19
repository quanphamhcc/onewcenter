using Microsoft.Data.Sqlite;
using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for <see cref="SlidePresentationService"/> (CRUD + Duplicate, SQLite-backed via the
/// shared app.db, SlidePresentations table). Each test injects a temp app.db via the ctor and
/// clears SQLite connection pools in teardown so the temp .db file can be deleted on Windows
/// (mirrors MediaLibraryServiceTests discipline).
/// </summary>
public class SlidePresentationServiceTests : IDisposable
{
    private readonly string _tempFolder;
    private readonly string _dbPath;
    private readonly SlidePresentationService _service;

    public SlidePresentationServiceTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"SlidePresTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
        _dbPath = Path.Combine(_tempFolder, "app.db");
        _service = new SlidePresentationService(_dbPath);
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
    public async Task CreateAsync_AssignsIdAndDefaults_AndPersists()
    {
        var created = await _service.CreateAsync("Sunday Welcome");

        Assert.False(string.IsNullOrEmpty(created.Id));
        Assert.Equal("Sunday Welcome", created.Name);
        Assert.Empty(created.Slides);
        Assert.NotEqual(default, created.CreatedAt);
        Assert.NotEqual(default, created.UpdatedAt);

        // Re-read via GetById and GetAll to confirm it persisted (SlideCount promoted to 0).
        var fetched = await _service.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal("Sunday Welcome", fetched!.Name);
        Assert.Empty(fetched.Slides);

        var all = await _service.GetAllAsync();
        Assert.Single(all);
        Assert.Equal(created.Id, all[0].Id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_BlankName_DefaultsToNewPresentation(string name)
    {
        var created = await _service.CreateAsync(name);

        Assert.Equal("New Presentation", created.Name);
    }

    [Fact]
    public async Task GetAllAsync_ReturnsMostRecentlyUpdatedFirst()
    {
        var first = await _service.CreateAsync("First");
        await Task.Delay(10);
        var second = await _service.CreateAsync("Second");

        // Update the OLDER one so it becomes the most-recently-updated.
        await Task.Delay(10);
        await _service.UpdateAsync(first);

        var all = await _service.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal(first.Id, all[0].Id);
        Assert.Equal(second.Id, all[1].Id);
    }

    [Fact]
    public async Task UpdateAsync_RederivesSlideCount_AndAdvancesUpdatedAt()
    {
        var created = await _service.CreateAsync("Deck");
        var originalUpdatedAt = (await _service.GetByIdAsync(created.Id))!.UpdatedAt;

        await Task.Delay(10);
        created.Slides.Add(new PresentationSlide { Order = 0, Text = "Slide one" });
        created.Slides.Add(new PresentationSlide { Order = 1, Text = "Slide two" });
        await _service.UpdateAsync(created);

        var fetched = await _service.GetByIdAsync(created.Id);
        Assert.NotNull(fetched);
        Assert.Equal(2, fetched!.Slides.Count);
        Assert.True(fetched.UpdatedAt > originalUpdatedAt);

        // The promoted SlideCount column must match Slides.Count: GetAll orders by it-free, but
        // re-reading the row proves the serialized list round-tripped the two slides.
        var all = await _service.GetAllAsync();
        Assert.Equal(2, all.Single(p => p.Id == created.Id).Slides.Count);
    }

    [Fact]
    public async Task DuplicateAsync_DeepClones_WithNewIds_AndDoesNotMutateOriginal()
    {
        var original = await _service.CreateAsync("Original");
        original.Slides.Add(new PresentationSlide { Order = 0, Text = "A" });
        original.Slides.Add(new PresentationSlide { Order = 1, Text = "B" });
        await _service.UpdateAsync(original);

        var copy = await _service.DuplicateAsync(original.Id);

        Assert.NotEqual(original.Id, copy.Id);
        Assert.Equal("Original (Copy)", copy.Name);
        Assert.Equal(2, copy.Slides.Count);

        // Each copied slide has a brand-new id (no id shared with the original's slides).
        var originalSlideIds = original.Slides.Select(s => s.Id).ToHashSet();
        Assert.All(copy.Slides, s => Assert.DoesNotContain(s.Id, originalSlideIds));

        // The original is unchanged — two rows now exist.
        var all = await _service.GetAllAsync();
        Assert.Equal(2, all.Count);
        var fetchedOriginal = await _service.GetByIdAsync(original.Id);
        Assert.Equal("Original", fetchedOriginal!.Name);
    }

    [Fact]
    public async Task DuplicateAsync_UnknownId_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _service.DuplicateAsync("does-not-exist"));
    }

    [Fact]
    public async Task DeleteAsync_RemovesRow()
    {
        var created = await _service.CreateAsync("ToDelete");

        await _service.DeleteAsync(created.Id);

        Assert.Null(await _service.GetByIdAsync(created.Id));
        Assert.Empty(await _service.GetAllAsync());
    }

    [Fact]
    public async Task DeleteAsync_UnknownId_IsNoOp_DoesNotThrow()
    {
        var created = await _service.CreateAsync("Keep");

        await _service.DeleteAsync("does-not-exist");

        // The existing row is untouched.
        Assert.Single(await _service.GetAllAsync());
    }

    [Fact]
    public async Task GetByIdAsync_UnknownId_ReturnsNull()
    {
        Assert.Null(await _service.GetByIdAsync("nope"));
    }

    [Fact]
    public async Task GetAllAsync_SkipsCorruptedDataJsonRow()
    {
        var good = await _service.CreateAsync("Good");

        // Hand-insert a row with invalid JSON directly into the temp db.
        using (var connection = new SqliteConnection($"Data Source={_dbPath}"))
        {
            connection.Open();
            using var cmd = connection.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO SlidePresentations (Id, Name, SlideCount, CreatedAt, UpdatedAt, DataJson)
                VALUES (@Id, @Name, 0, @CreatedAt, @UpdatedAt, @DataJson)";
            cmd.Parameters.AddWithValue("@Id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@Name", "Corrupt");
            cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@UpdatedAt", DateTime.UtcNow.ToString("o"));
            cmd.Parameters.AddWithValue("@DataJson", "{ not valid json ]");
            cmd.ExecuteNonQuery();
        }

        var all = await _service.GetAllAsync();

        // The corrupt row is skipped; the valid one is still returned.
        Assert.Single(all);
        Assert.Equal(good.Id, all[0].Id);
    }

    [Fact]
    public async Task SchemaInit_IsIdempotent_AcrossInstances()
    {
        var created = await _service.CreateAsync("Shared");

        // A second service instance against the same temp db must initialize and read fine.
        var second = new SlidePresentationService(_dbPath);
        var all = await second.GetAllAsync();

        Assert.Single(all);
        Assert.Equal(created.Id, all[0].Id);
    }
}
