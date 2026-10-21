using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure, testable mapping between the slide-item editor's two Auto-advance dropdowns
/// (the "after" trigger condition and the "when complete" go-to target) and the existing
/// <see cref="ServiceItemSettings"/> fields. No new storage format: the labels fold onto
/// <see cref="ServiceItemSettings.AutoAdvanceAfterCondition"/>,
/// <see cref="ServiceItemSettings.AutoAdvanceGoTo"/> and
/// <see cref="ServiceItemSettings.GoToItemId"/>.
///
/// The after-condition save delegates to <see cref="BulkEditService.ParseAfterCondition"/> so the
/// slide editor, the per-item panel, and bulk edit all agree on the label→enum mapping.
/// Labels are sentence case per the approved design ("Specific item", "Next item").
/// </summary>
public static class SlideAutoAdvanceMap
{
    // ─── After-condition axis ─────────────────────────────────────

    /// <summary>Disabled placeholder shown when auto-advance is off.</summary>
    public const string AfterDisabled = "Disabled";
    public const string AfterTimeDelay = "Time Delay";
    public const string AfterEndOfMedia = "End of Media";

    // ─── When-complete axis ───────────────────────────────────────

    public const string GoToDoNothing = "Do nothing";
    public const string GoToNextItem = "Next item";
    public const string GoToSpecificItem = "Specific item";
    public const string GoToLoop = "Loop";

    /// <summary>
    /// Save the after-condition dropdown label onto <paramref name="settings"/>.
    /// Delegates to <see cref="BulkEditService.ParseAfterCondition"/> so "Time Delay" → Timer,
    /// "End of Media" → AudioEnd, and "Disabled"/unknown → Manual.
    /// </summary>
    public static void SaveAfter(ServiceItemSettings settings, string? afterLabel)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        settings.AutoAdvanceAfterCondition = BulkEditService.ParseAfterCondition(afterLabel);
    }

    /// <summary>
    /// Save the when-complete dropdown label (and optional target id) onto
    /// <paramref name="settings"/>. "Do nothing" → NextSlide, "Next item" → NextItem,
    /// "Specific item" → SpecificItem, "Loop" → Loop. <see cref="ServiceItemSettings.GoToItemId"/>
    /// is set to <paramref name="specificItemId"/> only for "Specific item"; otherwise cleared.
    /// </summary>
    public static void SaveWhenComplete(ServiceItemSettings settings, string? goToLabel, string? specificItemId)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));

        var goTo = ToGoTo(goToLabel);
        settings.AutoAdvanceGoTo = goTo;
        settings.GoToItemId = goTo == AutoAdvanceGoTo.SpecificItem ? specificItemId : null;
    }

    /// <summary>
    /// Load the after-condition dropdown label from <paramref name="settings"/>. Timer → "Time Delay";
    /// AudioEnd and VideoEnd both fold to "End of Media"; Manual → "Disabled".
    /// </summary>
    public static string AfterLabel(ServiceItemSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        return settings.AutoAdvanceAfterCondition switch
        {
            AutoAdvanceAfterCondition.Timer => AfterTimeDelay,
            AutoAdvanceAfterCondition.AudioEnd => AfterEndOfMedia,
            AutoAdvanceAfterCondition.VideoEnd => AfterEndOfMedia,
            _ => AfterDisabled
        };
    }

    /// <summary>
    /// Load the when-complete dropdown label from <paramref name="settings"/>. NextSlide → "Do nothing",
    /// NextItem → "Next item", SpecificItem → "Specific item", Loop → "Loop".
    /// </summary>
    public static string GoToLabel(ServiceItemSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        return settings.AutoAdvanceGoTo switch
        {
            AutoAdvanceGoTo.NextItem => GoToNextItem,
            AutoAdvanceGoTo.SpecificItem => GoToSpecificItem,
            AutoAdvanceGoTo.Loop => GoToLoop,
            _ => GoToDoNothing
        };
    }

    /// <summary>
    /// Whether the "when complete" dropdown is enabled: it is meaningless while auto-advance is
    /// "Disabled", so the predicate is simply afterLabel != "Disabled".
    /// </summary>
    public static bool WhenCompleteEnabled(string afterLabel) => afterLabel != AfterDisabled;

    private static AutoAdvanceGoTo ToGoTo(string? goToLabel) => goToLabel switch
    {
        GoToNextItem => AutoAdvanceGoTo.NextItem,
        GoToSpecificItem => AutoAdvanceGoTo.SpecificItem,
        GoToLoop => AutoAdvanceGoTo.Loop,
        _ => AutoAdvanceGoTo.NextSlide
    };
}
