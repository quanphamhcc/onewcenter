using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Manages alert templates and alert display.
/// CRUD for alert templates stored in alert-templates.json.
/// Fires events when alerts are shown/dismissed for output rendering.
/// </summary>
public class AlertService : IAlertService
{
    private readonly string _templatesFilePath;
    private List<AlertTemplate> _templates = new();
    private bool _loaded = false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    /// <inheritdoc />
    public event EventHandler<AlertTemplate>? AlertShown;

    /// <inheritdoc />
    public event EventHandler? AlertDismissed;

    /// <summary>
    /// Whether an alert is currently being displayed.
    /// </summary>
    public bool IsAlertActive { get; private set; }

    /// <summary>
    /// The currently displayed alert (null if none).
    /// </summary>
    public AlertTemplate? CurrentAlert { get; private set; }

    public AlertService(string templatesFilePath)
    {
        _templatesFilePath = templatesFilePath;
    }

    /// <inheritdoc />
    public Task ShowAlertAsync(AlertTemplate template)
    {
        CurrentAlert = template;
        IsAlertActive = true;
        AlertShown?.Invoke(this, template);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DismissAlertAsync()
    {
        CurrentAlert = null;
        IsAlertActive = false;
        AlertDismissed?.Invoke(this, EventArgs.Empty);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<AlertTemplate> SaveTemplateAsync(AlertTemplate template)
    {
        await EnsureLoadedAsync();

        if (string.IsNullOrEmpty(template.Id))
            template.Id = Guid.NewGuid().ToString();

        var existing = _templates.FindIndex(t => t.Id == template.Id);
        if (existing >= 0)
            _templates[existing] = template;
        else
            _templates.Add(template);

        await SaveAllTemplatesAsync();
        return template;
    }

    /// <inheritdoc />
    public async Task<List<AlertTemplate>> GetTemplatesAsync()
    {
        await EnsureLoadedAsync();
        return new List<AlertTemplate>(_templates);
    }

    /// <inheritdoc />
    public async Task DeleteTemplateAsync(string templateId)
    {
        await EnsureLoadedAsync();

        var template = _templates.FirstOrDefault(t => t.Id == templateId);
        if (template == null) return;

        _templates.Remove(template);
        await SaveAllTemplatesAsync();
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded) return;

        if (File.Exists(_templatesFilePath))
        {
            try
            {
                var json = await File.ReadAllTextAsync(_templatesFilePath);
                _templates = JsonSerializer.Deserialize<List<AlertTemplate>>(json, JsonOptions) ?? new();
            }
            catch
            {
                _templates = new List<AlertTemplate>();
            }
        }

        _loaded = true;
    }

    private async Task SaveAllTemplatesAsync()
    {
        var dir = Path.GetDirectoryName(_templatesFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(_templates, JsonOptions);
        await File.WriteAllTextAsync(_templatesFilePath, json);
    }
}
