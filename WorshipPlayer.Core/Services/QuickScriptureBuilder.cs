using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Models.Bible;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Outcome of a Quick Scripture build (dot2-T13 / R7.3): either slides are
/// ready to present, or the chosen translation has no verse data for the
/// reference — a state the caller must be able to tell apart from success so
/// it can warn the operator instead of presenting nothing.
/// </summary>
public enum QuickScriptureStatus
{
    /// <summary>Verses were found and presentation slides were generated.</summary>
    Success,

    /// <summary>The translation has no verse data for the reference.</summary>
    NoVerses
}

/// <summary>
/// Result of <see cref="QuickScriptureBuilder.Build"/>: on success the slides
/// to present; on <see cref="QuickScriptureStatus.NoVerses"/> an empty slide
/// list (never null).
/// </summary>
public sealed class QuickScriptureResult
{
    internal QuickScriptureResult(QuickScriptureStatus status, IReadOnlyList<Slide> slides)
    {
        Status = status;
        Slides = slides;
    }

    public QuickScriptureStatus Status { get; }

    public IReadOnlyList<Slide> Slides { get; }

    public bool IsSuccess => Status == QuickScriptureStatus.Success;
}

/// <summary>
/// Pure builder behind Quick Scripture (dot2-T13 / R7.3): turns a parsed
/// <see cref="ScriptureReference"/> plus the verses fetched for it from one
/// translation into presentation-ready slides, delegating the actual slide
/// splitting to the ONE <see cref="ScriptureSlideGenerator"/>. Keeping this
/// logic in Core (out of the WPF panel) makes the present path unit-testable
/// and gives the no-data case a distinguishable status instead of an
/// ambiguous empty list.
/// </summary>
public static class QuickScriptureBuilder
{
    private static readonly ScriptureSlideGenerator Generator = new();

    /// <summary>
    /// Build the slides for a quick present. A null/empty verse list — or
    /// verses that yield no presentable slide — returns
    /// <see cref="QuickScriptureStatus.NoVerses"/> with an empty slide list.
    /// </summary>
    public static QuickScriptureResult Build(
        ScriptureReference reference,
        IReadOnlyList<BibleVerse>? verses,
        ScriptureSlideGenerator.SlideGenerationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (verses == null || verses.Count == 0)
        {
            return new QuickScriptureResult(
                QuickScriptureStatus.NoVerses, Array.Empty<Slide>());
        }

        var slides = Generator.GenerateSlides(verses.ToList(), reference, options);

        // Verses with no presentable text yield no slides — the same no-data
        // outcome from the operator's point of view.
        if (slides.Count == 0)
        {
            return new QuickScriptureResult(
                QuickScriptureStatus.NoVerses, Array.Empty<Slide>());
        }

        return new QuickScriptureResult(QuickScriptureStatus.Success, slides);
    }
}
