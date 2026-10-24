using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Bible;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// One scripture passage — a parsed reference plus the verses fetched for it from
/// one translation. The unit of input for mapping scripture into Slides-library
/// presentations (dot2-T14 / R7.4).
/// </summary>
public sealed record ScripturePassage(
    ScriptureReference Reference,
    IReadOnlyList<BibleVerse>? Verses);

/// <summary>
/// Pure (UI-free) mapping of scripture passages into Slides-library presentations
/// (dot2-T14 / R7.4): turns parsed references + their verses into authored
/// <see cref="PresentationSlide"/>s and builds/appends <see cref="SlidePresentation"/>s.
///
/// The slide splitting always goes through the single <see cref="ScriptureSlideGenerator"/>
/// — the same splitter Quick Scripture (T13) uses — so library slides match what the
/// operator previewed in the Scripture panel. Each generated slide keeps its
/// reference as the subtitle so the presentation reads independently of the panel.
/// </summary>
public static class ScriptureToSlidesMapper
{
    private static readonly ScriptureSlideGenerator Generator = new();

    /// <summary>
    /// Map one passage to library slides. An empty/null verse list yields no slides
    /// (the panel only offers Create Slides for a successful lookup, which already
    /// excluded the no-data state).
    /// </summary>
    public static List<PresentationSlide> MapPassage(
        ScripturePassage passage,
        ScriptureSlideGenerator.SlideGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(passage);
        ArgumentNullException.ThrowIfNull(passage.Reference);

        var verses = (passage.Verses ?? Array.Empty<BibleVerse>()).ToList();
        var runtime = Generator.GenerateSlides(verses, passage.Reference, options);

        return runtime.Select(s => new PresentationSlide
        {
            Order = s.Order,
            Text = s.Content?.Text ?? "",
            Subtitle = s.Content?.Subtitle ?? ""
        }).ToList();
    }

    /// <summary>
    /// Create a new library presentation from one or more passages, appended in
    /// order: passage 2's slides continue after passage 1's (no verse mixing), so
    /// non-contiguous passages can be collected into one presentation.
    /// </summary>
    public static SlidePresentation CreatePresentation(
        string? name,
        IReadOnlyList<ScripturePassage>? passages,
        ScriptureSlideGenerator.SlideGenerationOptions? options = null)
    {
        var presentation = new SlidePresentation
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Scripture" : name.Trim()
        };

        if (passages != null)
        {
            foreach (var passage in passages)
                AppendPassage(presentation, passage, options);
        }

        return presentation;
    }

    /// <summary>
    /// Append one passage's slides to the end of an existing presentation; slide
    /// order continues from the current last slide.
    /// </summary>
    public static void AppendPassage(
        SlidePresentation presentation,
        ScripturePassage passage,
        ScriptureSlideGenerator.SlideGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(presentation);

        var slides = MapPassage(passage, options);
        var nextOrder = presentation.Slides.Count == 0
            ? 0
            : presentation.Slides.Max(s => s.Order) + 1;

        foreach (var slide in slides)
            slide.Order = nextOrder++;

        presentation.Slides.AddRange(slides);
        presentation.UpdatedAt = DateTime.UtcNow;
    }
}
