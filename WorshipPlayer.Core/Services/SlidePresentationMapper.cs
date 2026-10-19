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
/// SNAPSHOT (not link): the presentation's <see cref="PresentationSlide"/>s are converted into
/// the item's runtime <see cref="Slide"/> list at add time and stored on
/// <see cref="ServiceItem.Slides"/>. The center render path (OperatorWindow.GetSlidesForItem)
/// already returns <see cref="ServiceItem.Slides"/> for non-Song items, and
/// <see cref="ServiceItem.Slides"/> already round-trips through the ServiceOrder JSON save path,
/// so snapshotting needs zero changes to the model, store, or renderer. Tradeoff: later edits to
/// the source presentation do NOT retro-update an already-added item.
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
    private static List<Slide> ToSlides(List<PresentationSlide>? slides)
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
                Content = new SlideContent { Text = ps.Text ?? "" }
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
