using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services.Interfaces;

/// <summary>
/// CRUD for Slides-library presentations (<see cref="SlidePresentation"/>), persisted as rows in
/// the shared SQLite application database (app.db, SlidePresentations table). Mirrors the
/// interface-in-Interfaces / impl-in-Services placement of <see cref="IMediaLibraryService"/>.
/// </summary>
public interface ISlidePresentationService
{
    /// <summary>All presentations, most-recently-updated first.</summary>
    Task<List<SlidePresentation>> GetAllAsync();

    /// <summary>The presentation with the given id, or null if none exists.</summary>
    Task<SlidePresentation?> GetByIdAsync(string id);

    /// <summary>
    /// Create and persist a new, empty presentation. A blank/whitespace
    /// <paramref name="name"/> is normalized to "New Presentation".
    /// </summary>
    Task<SlidePresentation> CreateAsync(string name);

    /// <summary>
    /// Persist changes to an existing presentation. Re-stamps UpdatedAt and re-derives SlideCount.
    /// </summary>
    Task UpdateAsync(SlidePresentation presentation);

    /// <summary>Delete the presentation with the given id. No-op if it does not exist.</summary>
    Task DeleteAsync(string id);

    /// <summary>
    /// Deep-clone the presentation with the given id (new ids, " (Copy)" name) and persist the copy.
    /// Throws <see cref="KeyNotFoundException"/> if no presentation has that id.
    /// </summary>
    Task<SlidePresentation> DuplicateAsync(string id);
}
