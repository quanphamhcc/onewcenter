using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Manages slide templates with CRUD operations.
/// Provides 3 built-in templates that cannot be deleted:
/// "Default Centered", "Lower Third", and "Trendy Line".
/// Loads and saves custom templates from a templates folder as JSON.
/// </summary>
public class TemplateService : ITemplateService
{
    private readonly string _templatesFolder;
    private readonly List<SlideTemplate> _templates = new();
    private bool _loaded = false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    // IDs for built-in templates (stable across sessions)
    private const string DefaultCenteredId = "builtin-default-centered";
    private const string LowerThirdId = "builtin-lower-third";
    private const string TrendyLineId = "builtin-trendy-line";

    public TemplateService(string templatesFolder)
    {
        _templatesFolder = templatesFolder;
    }

    /// <inheritdoc />
    public async Task<List<SlideTemplate>> GetAllAsync()
    {
        await EnsureLoadedAsync();
        return new List<SlideTemplate>(_templates);
    }

    /// <inheritdoc />
    public async Task<SlideTemplate?> GetByIdAsync(string templateId)
    {
        await EnsureLoadedAsync();
        return _templates.FirstOrDefault(t => t.Id == templateId);
    }

    /// <inheritdoc />
    public async Task<List<SlideTemplate>> GetByCategoryAsync(TemplateCategory category)
    {
        await EnsureLoadedAsync();
        return _templates.Where(t => t.TemplateCategory == category).ToList();
    }

    /// <inheritdoc />
    public async Task<SlideTemplate> CreateAsync(SlideTemplate template)
    {
        await EnsureLoadedAsync();

        if (string.IsNullOrEmpty(template.Id))
            template.Id = Guid.NewGuid().ToString();

        template.IsBuiltIn = false;
        _templates.Add(template);
        await SaveTemplateToFileAsync(template);

        return template;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(SlideTemplate template)
    {
        await EnsureLoadedAsync();

        var existing = _templates.FindIndex(t => t.Id == template.Id);
        if (existing < 0)
            throw new InvalidOperationException($"Template '{template.Id}' not found.");

        // Preserve built-in flag
        var isBuiltIn = _templates[existing].IsBuiltIn;
        template.IsBuiltIn = isBuiltIn;
        _templates[existing] = template;

        if (!isBuiltIn)
        {
            await SaveTemplateToFileAsync(template);
        }
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string templateId)
    {
        await EnsureLoadedAsync();

        var template = _templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null)
            return false;

        // Built-in templates cannot be deleted
        if (template.IsBuiltIn)
            return false;

        _templates.Remove(template);

        // Delete from disk
        var filePath = GetTemplateFilePath(template);
        if (File.Exists(filePath))
            File.Delete(filePath);

        return true;
    }

    /// <inheritdoc />
    public async Task<SlideTemplate> DuplicateAsync(string templateId, string newName)
    {
        await EnsureLoadedAsync();

        var source = _templates.FirstOrDefault(t => t.Id == templateId);
        if (source == null)
            throw new InvalidOperationException($"Template '{templateId}' not found.");

        // Deep copy via serialization
        var json = JsonSerializer.Serialize(source, JsonOptions);
        var duplicate = JsonSerializer.Deserialize<SlideTemplate>(json, JsonOptions)!;

        duplicate.Id = Guid.NewGuid().ToString();
        duplicate.Name = newName;
        duplicate.IsBuiltIn = false;

        _templates.Add(duplicate);
        await SaveTemplateToFileAsync(duplicate);

        return duplicate;
    }

    /// <inheritdoc />
    public async Task<List<SlideTemplate>> GetBuiltInTemplatesAsync()
    {
        await EnsureLoadedAsync();
        return _templates.Where(t => t.IsBuiltIn).ToList();
    }

    /// <inheritdoc />
    public async Task ApplyToItemAsync(string templateId, string itemId)
    {
        await EnsureLoadedAsync();

        var template = _templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null)
            throw new InvalidOperationException($"Template '{templateId}' not found.");

        // The actual application to an item would be handled by the caller
        // using the template data. This validates the template exists.
        await Task.CompletedTask;
    }

    /// <summary>
    /// Get the 3 built-in templates.
    /// </summary>
    public static List<SlideTemplate> GetBuiltInTemplates()
    {
        return new List<SlideTemplate>
        {
            new SlideTemplate
            {
                Id = DefaultCenteredId,
                Name = "Default Centered",
                IsBuiltIn = true,
                FontFamily = "Segoe UI",
                FontSize = 60,
                FontWeight = "Bold",
                FontColor = "#FFFFFF",
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HasShadow = true,
                ShadowColor = "#000000",
                ShadowBlur = 4.0,
                HasOutline = false,
                MarginTop = 40,
                MarginLeft = 60,
                MarginRight = 60,
                MarginBottom = 40,
                LineHeight = 1.3,
                TemplateCategory = TemplateCategory.General
            },
            new SlideTemplate
            {
                Id = LowerThirdId,
                Name = "Lower Third",
                IsBuiltIn = true,
                FontFamily = "Segoe UI",
                FontSize = 48,
                FontWeight = "Bold",
                FontColor = "#FFFFFF",
                HorizontalAlignment = TextAlignment.Left,
                VerticalAlignment = VerticalAlignment.Bottom,
                HasShadow = true,
                ShadowColor = "#000000",
                ShadowBlur = 6.0,
                HasOutline = false,
                MarginTop = 40,
                MarginLeft = 60,
                MarginRight = 60,
                MarginBottom = 80,
                LineHeight = 1.2,
                LineBackgroundEnabled = false,
                TemplateCategory = TemplateCategory.LowerThird
            },
            new SlideTemplate
            {
                Id = TrendyLineId,
                Name = "Trendy Line",
                IsBuiltIn = true,
                FontFamily = "Segoe UI",
                FontSize = 56,
                FontWeight = "Bold",
                FontColor = "#FFFFFF",
                HorizontalAlignment = TextAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                HasShadow = false,
                HasOutline = false,
                LineBackgroundEnabled = true,
                LineBackgroundColor = "#000000",
                LineBackgroundOpacity = 0.5,
                MarginTop = 40,
                MarginLeft = 80,
                MarginRight = 80,
                MarginBottom = 40,
                LineHeight = 1.6,
                TemplateCategory = TemplateCategory.General
            }
        };
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        // Always start with built-in templates
        _templates.AddRange(GetBuiltInTemplates());

        // Load custom templates from folder
        if (!Directory.Exists(_templatesFolder))
        {
            Directory.CreateDirectory(_templatesFolder);
            _loaded = true;
            return;
        }

        var files = Directory.GetFiles(_templatesFolder, "*.json");
        foreach (var file in files)
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var template = JsonSerializer.Deserialize<SlideTemplate>(json, JsonOptions);
                if (template != null && !_templates.Any(t => t.Id == template.Id))
                {
                    template.IsBuiltIn = false;
                    _templates.Add(template);
                }
            }
            catch
            {
                // Skip invalid template files
            }
        }

        _loaded = true;
    }

    private async Task SaveTemplateToFileAsync(SlideTemplate template)
    {
        if (!Directory.Exists(_templatesFolder))
            Directory.CreateDirectory(_templatesFolder);

        var filePath = GetTemplateFilePath(template);
        var json = JsonSerializer.Serialize(template, JsonOptions);
        await File.WriteAllTextAsync(filePath, json);
    }

    private string GetTemplateFilePath(SlideTemplate template)
    {
        var sanitizedName = SanitizeFileName(template.Name);
        return Path.Combine(_templatesFolder, $"{sanitizedName}.json");
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return sanitized.Replace(' ', '-').ToLowerInvariant();
    }
}
