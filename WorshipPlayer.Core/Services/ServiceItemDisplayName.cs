using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure (UI-free) resolver for the DISPLAY name of a service item.
///
/// Lives in Core (mirrors <see cref="SlidePresentationMapper"/>'s placement + rationale) so the
/// display-name rule is directly unit-testable — the Tests project is net8.0 and cannot reference
/// the WPF Presenter.
///
/// Rule: a LIVE-LINKED Slides item (<see cref="ServiceItemType.Slides"/> with a non-empty
/// <see cref="ServiceItem.SlidePresentationId"/>) shows the linked presentation's CURRENT
/// <see cref="SlidePresentation.Name"/>, resolved at DISPLAY time — so renaming the presentation
/// updates the sidebar row and center section header without rewriting the stored
/// <see cref="ServiceItem.Title"/>. Every other item (and a Slides item with no link, i.e. a legacy
/// snapshot) keeps its stored <see cref="ServiceItem.Title"/>. The caller resolves
/// <paramref name="linked"/> from the already-primed presentation cache and passes it in, so this
/// helper stays pure (it does not touch any cache or service).
/// </summary>
public static class ServiceItemDisplayName
{
    /// <summary>
    /// Resolve the name to DISPLAY for <paramref name="item"/>. Returns the linked presentation's
    /// trimmed <see cref="SlidePresentation.Name"/> for a Slides-link item when
    /// <paramref name="linked"/> is non-null with a non-blank name; otherwise returns
    /// <see cref="ServiceItem.Title"/> as-is (callers already handle an empty title). Falls back to
    /// the stored Title when the presentation is missing/deleted (<paramref name="linked"/> null) so
    /// there is no crash. Throws <see cref="ArgumentNullException"/> when <paramref name="item"/> is
    /// null (matches <see cref="SlidePresentationMapper"/>'s null-guard convention).
    /// </summary>
    public static string ResolveDisplayName(ServiceItem item, SlidePresentation? linked)
    {
        if (item == null) throw new ArgumentNullException(nameof(item));

        if (item.Type == ServiceItemType.Slides
            && !string.IsNullOrEmpty(item.SlidePresentationId)
            && linked != null
            && !string.IsNullOrWhiteSpace(linked.Name))
        {
            return linked.Name.Trim();
        }

        return item.Title;
    }
}
