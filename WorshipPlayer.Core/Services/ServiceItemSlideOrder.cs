using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure per-service-item slide ordering (đợt 2 T10 / R6.2): applies a
/// <see cref="ServiceItem"/>'s OWN slide order — a list of slide ids stored on the
/// item — on top of the slide list the pipeline already resolved for that item
/// (song-level Custom Slide Order T9 → original order). Resolution priority is
/// therefore T10 (item) &gt; T9 (song) &gt; original, exactly as R6.3 requires,
/// and this type is the single place that priority is enforced: callers first
/// resolve the item's slides through the existing pipeline, then pass them here.
///
/// Semantics of <see cref="Apply"/>:
///   • Order null (not set — every pre-T10 item) → the slides unchanged, same
///     objects in the same order (byte-for-byte pre-T10 behavior).
///   • Ids in the order that match a slide are emitted in order sequence; an id
///     listed twice emits the slide twice (repeat is a legitimate play order).
///   • Ids that no longer match any slide (the slide was deleted after the order
///     was authored) are SKIPPED — never an exception (R6 edge case).
///   • Slides whose id is not referenced by the order at all (newly generated
///     after the order was set, e.g. lyrics edited) are appended at the end in
///     their original relative order (R6 edge case).
///
/// The edit helpers (<see cref="CaptureOrder"/>, <see cref="Move"/>,
/// <see cref="Duplicate"/>) are equally pure: they never mutate their inputs and
/// never touch any <see cref="Song"/> — the isolation guarantee of R6.2 (editing
/// item A cannot change the song or item B) is structural, not conventional.
/// NOT applied to live-linked Slides items (Type = Slides with a
/// SlidePresentationId): those follow their presentation (T10 scope exclusion);
/// callers simply do not call Apply for them.
/// </summary>
public static class ServiceItemSlideOrder
{
    /// <summary>True when the item carries an explicit per-item slide order.</summary>
    public static bool HasCustomOrder(ServiceItem? item) => item?.SlideOrder != null;

    /// <summary>
    /// Apply an item's stored slide order to its resolved slides. See the class
    /// remarks for the exact semantics. Returns a NEW list; the slide objects
    /// themselves are shared, never copied or mutated.
    /// </summary>
    public static List<Slide> Apply(IReadOnlyList<Slide>? slides, IReadOnlyList<string>? orderIds)
    {
        if (slides == null || slides.Count == 0)
            return new List<Slide>();

        // No per-item order → pre-T10 behavior, identical sequence.
        if (orderIds == null)
            return new List<Slide>(slides);

        var byId = new Dictionary<string, Slide>(StringComparer.Ordinal);
        foreach (var slide in slides)
        {
            if (slide == null || string.IsNullOrEmpty(slide.Id))
                continue;
            byId.TryAdd(slide.Id, slide); // first occurrence wins for duplicate ids
        }

        var result = new List<Slide>(slides.Count);
        var emitted = new HashSet<Slide>();
        foreach (var id in orderIds)
        {
            if (string.IsNullOrEmpty(id))
                continue;
            if (byId.TryGetValue(id, out var slide))
            {
                result.Add(slide);
                emitted.Add(slide);
            }
            // Dead id (slide deleted after the order was set) → skip silently.
        }

        // Unreferenced slides (new since the order was authored) keep their
        // original relative order at the end.
        foreach (var slide in slides)
        {
            if (slide != null && !emitted.Contains(slide))
                result.Add(slide);
        }

        return result;
    }

    /// <summary>Convenience overload: apply the item's own stored order.</summary>
    public static List<Slide> ApplyForItem(ServiceItem? item, IReadOnlyList<Slide>? slides) =>
        Apply(slides, item?.SlideOrder);

    /// <summary>
    /// Materialize an explicit order equal to the slides' CURRENT sequence —
    /// the starting point when the operator first rearranges an item that has
    /// no stored order yet.
    /// </summary>
    public static List<string> CaptureOrder(IReadOnlyList<Slide>? slides)
    {
        var ids = new List<string>();
        if (slides == null)
            return ids;
        foreach (var slide in slides)
        {
            if (slide != null && !string.IsNullOrEmpty(slide.Id))
                ids.Add(slide.Id);
        }
        return ids;
    }

    /// <summary>
    /// Return a copy of <paramref name="orderIds"/> with the entry at
    /// <paramref name="index"/> moved by <paramref name="delta"/> positions
    /// (negative = earlier), clamped at both ends. An out-of-range index or a
    /// zero/clamped move returns an unchanged copy.
    /// </summary>
    public static List<string> Move(IReadOnlyList<string> orderIds, int index, int delta)
    {
        var result = orderIds == null ? new List<string>() : new List<string>(orderIds);
        if (index < 0 || index >= result.Count || delta == 0)
            return result;

        int target = Math.Clamp(index + delta, 0, result.Count - 1);
        if (target == index)
            return result;

        var id = result[index];
        result.RemoveAt(index);
        result.Insert(target, id);
        return result;
    }

    /// <summary>
    /// Duplicate one slide inside an item: the copy is a NEW slide object with a
    /// NEW id but identical content, placed immediately after the original both
    /// in the slide list and in the item's effective order, so after
    /// <see cref="Apply"/> the two sit side by side. When the item had no stored
    /// order, the returned order is the materialized current sequence (with the
    /// copy inserted). Returns the inputs unchanged (as copies) when the slide
    /// id is not found. Inputs are never mutated.
    /// </summary>
    public static (List<Slide> Slides, List<string> OrderIds) Duplicate(
        IReadOnlyList<Slide>? slides, IReadOnlyList<string>? orderIds, string? slideId)
    {
        var slideList = slides == null ? new List<Slide>() : new List<Slide>(slides);
        if (string.IsNullOrEmpty(slideId))
            return (slideList, orderIds == null ? CaptureOrder(slideList) : new List<string>(orderIds));

        int index = slideList.FindIndex(s => s != null && s.Id == slideId);
        if (index < 0)
            return (slideList, orderIds == null ? CaptureOrder(slideList) : new List<string>(orderIds));

        var copy = CloneWithNewId(slideList[index]);
        slideList.Insert(index + 1, copy);

        // Work on the EFFECTIVE order (what Apply currently displays for the
        // original list) so the copy lands right after the original even when
        // the stored order is only partial (some slides unreferenced).
        var effective = CaptureOrder(Apply(slides, orderIds));
        int pos = effective.IndexOf(slideId);
        if (pos >= 0)
            effective.Insert(pos + 1, copy.Id);
        else
            effective.Add(copy.Id);
        return (slideList, effective);
    }

    /// <summary>
    /// Deep-enough copy for duplication: new id, same type/timing/transition, and
    /// a copied <see cref="SlideContent"/> so editing the copy's text later can
    /// never alias the original's. The theme override reference is shared on
    /// purpose (themes are shared library objects everywhere else too).
    /// </summary>
    private static Slide CloneWithNewId(Slide source) => new()
    {
        Id = Guid.NewGuid().ToString(),
        Type = source.Type,
        Content = new SlideContent
        {
            Text = source.Content?.Text ?? "",
            Subtitle = source.Content?.Subtitle ?? "",
            Header = source.Content?.Header ?? "",
            Footer = source.Content?.Footer ?? "",
            BackgroundImage = source.Content?.BackgroundImage ?? "",
            ForegroundImage = source.Content?.ForegroundImage ?? ""
        },
        Order = source.Order,
        SectionName = source.SectionName,
        TimestampMs = source.TimestampMs,
        Transition = source.Transition,
        TransitionDurationMs = source.TransitionDurationMs,
        ThemeOverride = source.ThemeOverride
    };
}
