using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure (UI-free) projection of an authored library <see cref="SlidePresentation"/> into a
/// runtime <see cref="ServiceItem"/> of type <see cref="ServiceItemType.Slides"/>.
///
/// Mirrors <see cref="ServiceItemMapper"/>'s style and rationale: it lives in Core so the
/// Slides "Add to current service" conversion — the part that turns an editor presentation into
/// the slides the center grid renders — is directly unit-testable (the Tests project is net8.0
/// and cannot reference the WPF Presenter).
///
/// Two projection shapes are supported:
/// <list type="bullet">
/// <item>LIVE LINK (<see cref="ToServiceItemLink(SlidePresentation)"/>, current add path): the item
/// stores only <see cref="ServiceItem.SlidePresentationId"/> and leaves <see cref="ServiceItem.Slides"/>
/// empty. The renderer resolves the presentation by id and converts its CURRENT slides on demand via
/// <see cref="ToSlides(SlidePresentation)"/>, so later edits to the source presentation show live
/// (mirrors how Song items resolve their linked library song).</item>
/// <item>SNAPSHOT (<see cref="ToServiceItem(SlidePresentation)"/>, legacy): the presentation's
/// <see cref="PresentationSlide"/>s are converted into the item's runtime <see cref="Slide"/> list at
/// add time and stored on <see cref="ServiceItem.Slides"/>. Retained for backward compatibility —
/// a snapshot item with no link still renders its stored slides. Tradeoff: later edits to the source
/// presentation do NOT retro-update such an item.</item>
/// </list>
/// </summary>
public static class SlidePresentationMapper
{
    /// <summary>
    /// Convert a resolved <paramref name="presentation"/> into a slides <see cref="ServiceItem"/>.
    /// Sets <see cref="ServiceItemType.Slides"/>, a Title from the presentation Name (falling back
    /// to "Slides Item" when blank), and snapshots each <see cref="PresentationSlide"/> into a
    /// runtime <see cref="Slide"/> in <see cref="PresentationSlide.Order"/> order (stable by
    /// original index). A null/empty slide list yields a non-null empty <see cref="Slide"/> list.
    /// </summary>
    public static ServiceItem ToServiceItem(SlidePresentation presentation)
    {
        if (presentation == null) throw new ArgumentNullException(nameof(presentation));

        var item = new ServiceItem
        {
            Type = ServiceItemType.Slides,
            Title = BuildTitle(presentation.Name),
            Slides = ToSlides(presentation.Slides)
        };

        return item;
    }

    /// <summary>
    /// Produce the LIVE LINK form of a slides <see cref="ServiceItem"/> for a resolved
    /// <paramref name="presentation"/>. Sets <see cref="ServiceItemType.Slides"/>, a Title from the
    /// presentation Name (falling back to "Slides Item" when blank), and
    /// <see cref="ServiceItem.SlidePresentationId"/> = <paramref name="presentation"/>.Id. Crucially
    /// it does NOT snapshot slides — <see cref="ServiceItem.Slides"/> is left empty so the renderer
    /// resolves the presentation's CURRENT slides on demand and later edits show live.
    /// </summary>
    public static ServiceItem ToServiceItemLink(SlidePresentation presentation)
    {
        if (presentation == null) throw new ArgumentNullException(nameof(presentation));

        return new ServiceItem
        {
            Type = ServiceItemType.Slides,
            Title = BuildTitle(presentation.Name),
            SlidePresentationId = presentation.Id,
            Slides = new List<Slide>()
        };
    }

    /// <summary>
    /// Convert a resolved <paramref name="presentation"/>'s CURRENT slides into runtime
    /// <see cref="Slide"/>s. This is the on-demand converter the renderer calls for a live-linked
    /// Slides item, so a presentation edited after it was added shows its latest slides.
    /// A null/empty slide list yields a non-null empty list.
    /// </summary>
    public static List<Slide> ToSlides(SlidePresentation presentation)
    {
        if (presentation == null) throw new ArgumentNullException(nameof(presentation));

        return ToSlides(presentation.Slides);
    }

    /// <summary>
    /// Final Title for a slides item: the trimmed presentation name, else the "Slides Item"
    /// fallback (mirrors <see cref="ServiceItemMapper.BuildTitle"/>'s "{type} Item" fallback).
    /// </summary>
    private static string BuildTitle(string? name)
    {
        if (!string.IsNullOrWhiteSpace(name))
            return name.Trim();

        return "Slides Item";
    }

    /// <summary>
    /// Project authored slides into runtime slides, ordered by <see cref="PresentationSlide.Order"/>
    /// then by original index (stable). Text is preserved; an optional background image maps to
    /// <see cref="SlideContent.BackgroundImage"/>, and an optional hex background colour maps to a
    /// <see cref="Slide.ThemeOverride"/> solid-colour background. When neither background field is
    /// set, both are left unset so the renderer falls through to the theme/default background.
    /// </summary>
    public static List<Slide> ToSlides(List<PresentationSlide>? slides)
    {
        var result = new List<Slide>();
        if (slides == null || slides.Count == 0)
            return result;

        var ordered = slides
            .Select((ps, index) => (ps, index))
            .OrderBy(t => t.ps.Order)
            .ThenBy(t => t.index);

        foreach (var (ps, _) in ordered)
        {
            var slide = new Slide
            {
                Type = SlideType.Custom,
                Order = ps.Order,
                SectionName = "",
                Content = new SlideContent
                {
                    Text = ps.Text ?? "",
                    Subtitle = ps.Subtitle ?? ""
                }
            };

            if (!string.IsNullOrEmpty(ps.BackgroundImage))
                slide.Content.BackgroundImage = ps.BackgroundImage;

            if (!string.IsNullOrEmpty(ps.BackgroundColor))
            {
                slide.ThemeOverride = new SlideTheme
                {
                    BackgroundType = BackgroundType.SolidColor,
                    BackgroundColor = ps.BackgroundColor
                };
            }

            result.Add(slide);
        }

        return result;
    }
}
