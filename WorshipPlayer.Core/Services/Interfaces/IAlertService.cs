using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services.Interfaces;

/// <summary>
/// Manages alert display and alert template persistence.
/// </summary>
public interface IAlertService
{
    /// <summary>
    /// Show an alert on all enabled outputs.
    /// </summary>
    /// <param name="template">Alert template with text and styling.</param>
    Task ShowAlertAsync(AlertTemplate template);

    /// <summary>
    /// Dismiss the currently displayed alert.
    /// </summary>
    Task DismissAlertAsync();

    /// <summary>
    /// Save an alert template for reuse.
    /// </summary>
    Task<AlertTemplate> SaveTemplateAsync(AlertTemplate template);

    /// <summary>
    /// Get all saved alert templates.
    /// </summary>
    Task<List<AlertTemplate>> GetTemplatesAsync();

    /// <summary>
    /// Delete a saved alert template.
    /// </summary>
    Task DeleteTemplateAsync(string templateId);

    /// <summary>
    /// Event raised when an alert is shown.
    /// </summary>
    event EventHandler<AlertTemplate>? AlertShown;

    /// <summary>
    /// Event raised when an alert is dismissed.
    /// </summary>
    event EventHandler? AlertDismissed;
}
