using Xunit;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Presenter.Services;

namespace WorshipPlayer.Tests;

/// <summary>
/// Unit tests for ThemeManager service.
/// Tests loading, saving, default themes, and theme switching.
/// </summary>
public class ThemeManagerTests : IDisposable
{
    private readonly string _tempFolder;

    public ThemeManagerTests()
    {
        _tempFolder = Path.Combine(Path.GetTempPath(), $"ThemeManagerTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempFolder);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempFolder))
            Directory.Delete(_tempFolder, true);
    }

    [Fact]
    public void Constructor_InitializesWithDefaultThemes()
    {
        var manager = new ThemeManager();

        Assert.NotNull(manager.AvailableThemes);
        Assert.Equal(3, manager.AvailableThemes.Count);
        Assert.Contains(manager.AvailableThemes, t => t.Name == "Default Dark");
        Assert.Contains(manager.AvailableThemes, t => t.Name == "Clean White");
        Assert.Contains(manager.AvailableThemes, t => t.Name == "Gradient Blue");
    }

    [Fact]
    public void Constructor_SetsCurrentThemeToFirstDefault()
    {
        var manager = new ThemeManager();

        Assert.NotNull(manager.CurrentTheme);
        Assert.Equal("Default Dark", manager.CurrentTheme.Name);
        Assert.Equal("default-dark", manager.CurrentTheme.Id);
    }

    [Fact]
    public void GetBuiltInThemes_ReturnsThreeThemes()
    {
        var themes = ThemeManager.GetBuiltInThemes();

        Assert.Equal(3, themes.Count);
        Assert.Equal("default-dark", themes[0].Id);
        Assert.Equal("clean-white", themes[1].Id);
        Assert.Equal("gradient-blue", themes[2].Id);
    }

    [Fact]
    public void GetBuiltInThemes_DefaultDarkHasCorrectProperties()
    {
        var themes = ThemeManager.GetBuiltInThemes();
        var dark = themes[0];

        Assert.Equal("Default Dark", dark.Name);
        Assert.Equal(BackgroundType.SolidColor, dark.BackgroundType);
        Assert.Equal("#000000", dark.BackgroundColor);
        Assert.Equal("#FFFFFF", dark.PrimaryFontColor);
        Assert.True(dark.PrimaryFontBold);
        Assert.True(dark.PrimaryTextShadow);
    }

    [Fact]
    public void GetBuiltInThemes_CleanWhiteHasCorrectProperties()
    {
        var themes = ThemeManager.GetBuiltInThemes();
        var white = themes[1];

        Assert.Equal("Clean White", white.Name);
        Assert.Equal("#FFFFFF", white.BackgroundColor);
        Assert.Equal("#1A1A1A", white.PrimaryFontColor);
        Assert.False(white.PrimaryTextShadow);
    }

    [Fact]
    public void GetBuiltInThemes_GradientBlueHasCorrectProperties()
    {
        var themes = ThemeManager.GetBuiltInThemes();
        var blue = themes[2];

        Assert.Equal("Gradient Blue", blue.Name);
        Assert.Equal("#0A1628", blue.BackgroundColor);
        Assert.True(blue.UseGradientOverlay);
        Assert.Equal(0.6, blue.GradientOpacity);
    }

    [Fact]
    public void SetCurrentTheme_ChangesCurrentTheme()
    {
        var manager = new ThemeManager();

        manager.SetCurrentTheme("clean-white");

        Assert.Equal("Clean White", manager.CurrentTheme.Name);
        Assert.Equal("clean-white", manager.CurrentTheme.Id);
    }

    [Fact]
    public void SetCurrentTheme_FiresThemeChangedEvent()
    {
        var manager = new ThemeManager();
        SlideTheme? receivedTheme = null;
        manager.ThemeChanged += (sender, theme) => receivedTheme = theme;

        manager.SetCurrentTheme("gradient-blue");

        Assert.NotNull(receivedTheme);
        Assert.Equal("Gradient Blue", receivedTheme!.Name);
    }

    [Fact]
    public void SetCurrentTheme_WithInvalidId_DoesNotChange()
    {
        var manager = new ThemeManager();
        var originalTheme = manager.CurrentTheme;

        manager.SetCurrentTheme("nonexistent-id");

        Assert.Equal(originalTheme.Id, manager.CurrentTheme.Id);
    }

    [Fact]
    public void SetCurrentTheme_WithInvalidId_DoesNotFireEvent()
    {
        var manager = new ThemeManager();
        bool eventFired = false;
        manager.ThemeChanged += (sender, theme) => eventFired = true;

        manager.SetCurrentTheme("nonexistent-id");

        Assert.False(eventFired);
    }

    [Fact]
    public void GetThemeById_ReturnsCorrectTheme()
    {
        var manager = new ThemeManager();

        var theme = manager.GetThemeById("clean-white");

        Assert.NotNull(theme);
        Assert.Equal("Clean White", theme!.Name);
    }

    [Fact]
    public void GetThemeById_WithInvalidId_ReturnsNull()
    {
        var manager = new ThemeManager();

        var theme = manager.GetThemeById("not-a-real-id");

        Assert.Null(theme);
    }

    [Fact]
    public void LoadThemes_CreatesDirectoryIfNotExists()
    {
        var manager = new ThemeManager();
        var newFolder = Path.Combine(_tempFolder, "new_themes");

        manager.LoadThemes(newFolder);

        Assert.True(Directory.Exists(newFolder));
    }

    [Fact]
    public void LoadThemes_LoadsJsonFilesFromFolder()
    {
        var manager = new ThemeManager();

        // Create a custom theme file
        var customTheme = new SlideTheme
        {
            Id = "custom-test",
            Name = "Custom Test Theme",
            BackgroundColor = "#FF0000"
        };
        var json = System.Text.Json.JsonSerializer.Serialize(customTheme);
        File.WriteAllText(Path.Combine(_tempFolder, "custom.json"), json);

        manager.LoadThemes(_tempFolder);

        Assert.Equal(4, manager.AvailableThemes.Count); // 3 built-in + 1 custom
        Assert.Contains(manager.AvailableThemes, t => t.Name == "Custom Test Theme");
    }

    [Fact]
    public void LoadThemes_SkipsInvalidJsonFiles()
    {
        var manager = new ThemeManager();

        // Write invalid JSON
        File.WriteAllText(Path.Combine(_tempFolder, "invalid.json"), "not valid json {{{");

        manager.LoadThemes(_tempFolder);

        // Should still have only the 3 built-in themes
        Assert.Equal(3, manager.AvailableThemes.Count);
    }

    [Fact]
    public void LoadThemes_DoesNotDuplicateBuiltInThemes()
    {
        var manager = new ThemeManager();

        // Save a theme with same ID as a built-in
        var duplicate = new SlideTheme
        {
            Id = "default-dark",
            Name = "Default Dark Duplicate",
            BackgroundColor = "#111111"
        };
        var json = System.Text.Json.JsonSerializer.Serialize(duplicate);
        File.WriteAllText(Path.Combine(_tempFolder, "duplicate.json"), json);

        manager.LoadThemes(_tempFolder);

        // Should not add the duplicate
        Assert.Equal(3, manager.AvailableThemes.Count);
    }

    [Fact]
    public void SaveTheme_WritesJsonFile()
    {
        var manager = new ThemeManager();
        manager.LoadThemes(_tempFolder);

        var theme = new SlideTheme
        {
            Id = "save-test",
            Name = "Save Test",
            BackgroundColor = "#00FF00"
        };

        manager.SaveTheme(theme);

        var files = Directory.GetFiles(_tempFolder, "*.json");
        Assert.Single(files);
        Assert.Contains("save-test", File.ReadAllText(files[0]));
    }

    [Fact]
    public void SaveTheme_AddsToAvailableThemes()
    {
        var manager = new ThemeManager();
        manager.LoadThemes(_tempFolder);

        var theme = new SlideTheme
        {
            Id = "new-theme",
            Name = "New Theme",
            BackgroundColor = "#0000FF"
        };

        manager.SaveTheme(theme);

        Assert.Equal(4, manager.AvailableThemes.Count);
        Assert.Contains(manager.AvailableThemes, t => t.Id == "new-theme");
    }

    [Fact]
    public void SaveTheme_UpdatesExistingTheme()
    {
        var manager = new ThemeManager();
        manager.LoadThemes(_tempFolder);

        var theme = new SlideTheme
        {
            Id = "update-test",
            Name = "Original Name",
            BackgroundColor = "#000000"
        };
        manager.SaveTheme(theme);

        // Update the same theme
        theme.Name = "Updated Name";
        theme.BackgroundColor = "#FFFFFF";
        manager.SaveTheme(theme);

        var found = manager.GetThemeById("update-test");
        Assert.NotNull(found);
        Assert.Equal("Updated Name", found!.Name);
        Assert.Equal("#FFFFFF", found.BackgroundColor);
        // Should not duplicate
        Assert.Equal(4, manager.AvailableThemes.Count);
    }

    [Fact]
    public void SaveTheme_WithoutLoadThemes_DoesNotThrow()
    {
        var manager = new ThemeManager();

        var theme = new SlideTheme { Id = "test", Name = "Test" };

        // Should not throw - just won't write to disk since no folder is set
        manager.SaveTheme(theme);
    }
}
