using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services.Interfaces;

/// <summary>
/// Manages slide templates - CRUD, built-in templates, and application to items.
/// </summary>
public interface ITemplateService
{
    /// <summary>
    /// Get all available templates (built-in and custom).
    /// </summary>
    Task<List<SlideTemplate>> GetAllAsync();

    /// <summary>
    /// Get a template by ID.
    /// </summary>
    Task<SlideTemplate?> GetByIdAsync(string templateId);

    /// <summary>
    /// Get templates by category.
    /// </summary>
    Task<List<SlideTemplate>> GetByCategoryAsync(TemplateCategory category);

    /// <summary>
    /// Create a new template.
    /// </summary>
    Task<SlideTemplate> CreateAsync(SlideTemplate template);

    /// <summary>
    /// Update an existing template.
    /// </summary>
    Task UpdateAsync(SlideTemplate template);

    /// <summary>
    /// Delete a template. Built-in templates cannot be deleted.
    /// </summary>
    Task<bool> DeleteAsync(string templateId);

    /// <summary>
    /// Duplicate an existing template with a new name.
    /// </summary>
    Task<SlideTemplate> DuplicateAsync(string templateId, string newName);

    /// <summary>
    /// Get all built-in templates.
    /// </summary>
    Task<List<SlideTemplate>> GetBuiltInTemplatesAsync();

    /// <summary>
    /// Apply a template to a service item's slides.
    /// </summary>
    /// <param name="templateId">Template to apply.</param>
    /// <param name="itemId">Service item to apply the template to.</param>
    Task ApplyToItemAsync(string templateId, string itemId);
}
