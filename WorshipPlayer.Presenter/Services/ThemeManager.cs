using System.IO;
using System.Text.Json;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Manages slide themes for the presenter.
/// 
/// Loads/saves SlideTheme objects from a Themes/ folder,
/// provides built-in default themes, and notifies on theme change.
/// </summary>
public class ThemeManager
{
    private readonly List<SlideTheme> _themes = new();
    private SlideTheme _currentTheme;
    private string _themesFolder = "";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// The currently active theme.
    /// </summary>
    public SlideTheme CurrentTheme => _currentTheme;

    /// <summary>
    /// All available themes (built-in + user-loaded).
    /// </summary>
    public List<SlideTheme> AvailableThemes => new(_themes);

    /// <summary>
    /// Fired when the current theme changes.
    /// </summary>
    public event EventHandler<SlideTheme>? ThemeChanged;

    public ThemeManager()
    {
        // Initialize with default themes
        _themes.AddRange(GetBuiltInThemes());
        _currentTheme = _themes[0];
    }

    /// <summary>
    /// Load themes from a folder. Each .json file in the folder is a theme.
    /// Built-in themes are always available regardless of folder contents.
    /// </summary>
    public void LoadThemes(string folderPath)
    {
        _themesFolder = folderPath;

        if (!Directory.Exists(folderPath))
        {
            Directory.CreateDirectory(folderPath);
            return;
        }

        var files = Directory.GetFiles(folderPath, "*.json");
        foreach (var file in files)
        {
            try
            {
                var json = File.ReadAllText(file);
                var theme = JsonSerializer.Deserialize<SlideTheme>(json, JsonOptions);
                if (theme != null && !_themes.Any(t => t.Id == theme.Id))
                {
                    _themes.Add(theme);
                }
            }
            catch
            {
                // Skip invalid theme files
            }
        }
    }

    /// <summary>
    /// Save a theme to the themes folder as a JSON file.
    /// </summary>
    public void SaveTheme(SlideTheme theme)
    {
        if (string.IsNullOrEmpty(_themesFolder))
            return;

        if (!Directory.Exists(_themesFolder))
            Directory.CreateDirectory(_themesFolder);

        // Add or update in the list
        var existing = _themes.FindIndex(t => t.Id == theme.Id);
        if (existing >= 0)
            _themes[existing] = theme;
        else
            _themes.Add(theme);

        // Write to disk
        var filePath = Path.Combine(_themesFolder, $"{SanitizeFileName(theme.Name)}.json");
        var json = JsonSerializer.Serialize(theme, JsonOptions);
        File.WriteAllText(filePath, json);
    }

    /// <summary>
    /// Set the current theme by ID.
    /// </summary>
    public void SetCurrentTheme(string themeId)
    {
        var theme = _themes.FirstOrDefault(t => t.Id == themeId);
        if (theme != null)
        {
            _currentTheme = theme;
            ThemeChanged?.Invoke(this, _currentTheme);
        }
    }

    /// <summary>
    /// Get a theme by its ID.
    /// </summary>
    public SlideTheme? GetThemeById(string id)
    {
        return _themes.FirstOrDefault(t => t.Id == id);
    }

    /// <summary>
    /// Get the built-in default themes.
    /// </summary>
    public static List<SlideTheme> GetBuiltInThemes()
    {
        return new List<SlideTheme>
        {
            new SlideTheme
            {
                Id = "default-dark",
                Name = "Default Dark",
                BackgroundType = BackgroundType.SolidColor,
                BackgroundColor = "#000000",
                PrimaryFontFamily = "Segoe UI",
                PrimaryFontSize = 60,
                PrimaryFontColor = "#FFFFFF",
                PrimaryFontBold = true,
                PrimaryTextShadow = true,
                PrimaryTextShadowColor = "#000000",
                PrimaryTextShadowBlur = 6.0,
                SecondaryFontColor = "#CCCCCC",
                FooterFontColor = "#666666",
                UseGradientOverlay = false,
                LineSpacing = 1.3
            },
            new SlideTheme
            {
                Id = "clean-white",
                Name = "Clean White",
                BackgroundType = BackgroundType.SolidColor,
                BackgroundColor = "#FFFFFF",
                PrimaryFontFamily = "Segoe UI",
                PrimaryFontSize = 56,
                PrimaryFontColor = "#1A1A1A",
                PrimaryFontBold = true,
                PrimaryTextShadow = false,
                SecondaryFontColor = "#444444",
                FooterFontColor = "#999999",
                UseGradientOverlay = false,
                LineSpacing = 1.4
            },
            new SlideTheme
            {
                Id = "gradient-blue",
                Name = "Gradient Blue",
                BackgroundType = BackgroundType.SolidColor,
                BackgroundColor = "#0A1628",
                PrimaryFontFamily = "Segoe UI",
                PrimaryFontSize = 58,
                PrimaryFontColor = "#FFFFFF",
                PrimaryFontBold = true,
                PrimaryTextShadow = true,
                PrimaryTextShadowColor = "#001133",
                PrimaryTextShadowBlur = 8.0,
                SecondaryFontColor = "#88BBFF",
                FooterFontColor = "#5577AA",
                UseGradientOverlay = true,
                GradientColor = "#001144",
                GradientOpacity = 0.6,
                LineSpacing = 1.3
            }
        };
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Replace(' ', '-').ToLowerInvariant();
    }
}
