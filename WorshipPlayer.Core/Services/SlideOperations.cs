using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure, UI-free slide-list operations over a <see cref="List{PresentationSlide}"/>. Every method
/// mutates the passed list in place and keeps the <see cref="PresentationSlide.Order"/> values
/// dense and 0-based (Order == index) so the list is always safe to persist via
/// <see cref="SlidePresentationService.UpdateAsync"/>.
/// </summary>
/// <remarks>
/// Deliberately never reads or writes <see cref="PresentationSlide.BackgroundImage"/> or
/// <see cref="PresentationSlide.BackgroundColor"/> — those stay untouched (null in Stage 1).
/// Out-of-range indices are no-ops rather than throwing, so callers can bind directly to a
/// selected-index that may be -1.
/// </remarks>
public static class SlideOperations
{
    /// <summary>
    /// Append a new empty slide with <see cref="PresentationSlide.Order"/> set to the next
    /// position and return it.
    /// </summary>
    public static PresentationSlide AddSlide(List<PresentationSlide> slides)
    {
        var slide = new PresentationSlide { Order = slides.Count, Text = "" };
        slides.Add(slide);
        return slide;
    }

    /// <summary>
    /// Remove the slide at <paramref name="index"/> and renumber the remainder dense/0-based.
    /// Out-of-range index is a no-op.
    /// </summary>
    public static void DeleteSlide(List<PresentationSlide> slides, int index)
    {
        if (index < 0 || index >= slides.Count)
            return;

        slides.RemoveAt(index);
        Renumber(slides);
    }

    /// <summary>
    /// Swap the slide at <paramref name="index"/> with the one above it and renumber. No-op when
    /// the index is not a valid non-first position.
    /// </summary>
    public static void MoveUp(List<PresentationSlide> slides, int index)
    {
        if (index < 1 || index >= slides.Count)
            return;

        (slides[index - 1], slides[index]) = (slides[index], slides[index - 1]);
        Renumber(slides);
    }

    /// <summary>
    /// Swap the slide at <paramref name="index"/> with the one below it and renumber. No-op when
    /// the index is not a valid non-last position.
    /// </summary>
    public static void MoveDown(List<PresentationSlide> slides, int index)
    {
        if (index < 0 || index >= slides.Count - 1)
            return;

        (slides[index + 1], slides[index]) = (slides[index], slides[index + 1]);
        Renumber(slides);
    }

    /// <summary>
    /// Stable-sort the list by each slide's current <see cref="PresentationSlide.Order"/> and
    /// redensify to 0..n-1.
    /// </summary>
    public static void Normalize(List<PresentationSlide> slides)
    {
        var sorted = slides.OrderBy(s => s.Order).ToList();
        slides.Clear();
        slides.AddRange(sorted);
        Renumber(slides);
    }

    private static void Renumber(List<PresentationSlide> slides)
    {
        for (var i = 0; i < slides.Count; i++)
            slides[i].Order = i;
    }
}
