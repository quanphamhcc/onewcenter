using System.Text.Json.Serialization;

namespace WorshipPlayer.Core.Models;

/// <summary>
/// A library "Slides" presentation — an authored, named collection of slides managed from the
/// Presenter "Slides" tab. This is the Slides-library (editor) model, deliberately SEPARATE from
/// the runtime presenter-output <see cref="Slide"/> (which carries transition/timestamp/section
/// and backtrack-sync concerns). Keeping them apart stops presenter-runtime fields from leaking
/// into the library model and lets a later stage project <see cref="PresentationSlide"/> → runtime
/// <see cref="Slide"/> when a presentation is presented.
///
/// JSON-serializable (stored whole in the SlidePresentations.DataJson column). Forward-compatible:
/// STAGE 2 (editor) fills <see cref="Slides"/> and may set <see cref="ThumbnailPath"/> /
/// per-slide backgrounds; STAGE 3 (service item) references a presentation by its stable
/// <see cref="Id"/> — neither requires a model rewrite.
/// </summary>
public class SlidePresentation
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("slides")]
    public List<PresentationSlide> Slides { get; set; } = new();

    /// <summary>
    /// Optional path to a rendered thumbnail of the first slide. STAGE 2 (editor) renders and sets
    /// this; nullable so STAGE 1 rows round-trip unchanged (Stage 1 shows a placeholder tile).
    /// </summary>
    [JsonPropertyName("thumbnailPath")]
    public string? ThumbnailPath { get; set; }
}

/// <summary>
/// A single authored slide inside a <see cref="SlidePresentation"/>. Smaller and editor-focused
/// (no transition/timestamp/section/theme-override fields). Background members are optional and
/// default to null so STAGE 1 never sets them; STAGE 2 (editor) populates text and backgrounds.
/// </summary>
public class PresentationSlide
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [JsonPropertyName("order")]
    public int Order { get; set; }

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    /// <summary>Optional background image path (STAGE 2). Null in STAGE 1.</summary>
    [JsonPropertyName("backgroundImage")]
    public string? BackgroundImage { get; set; }

    /// <summary>Optional background colour as hex "#RRGGBB" (STAGE 2). Null in STAGE 1.</summary>
    [JsonPropertyName("backgroundColor")]
    public string? BackgroundColor { get; set; }
}
