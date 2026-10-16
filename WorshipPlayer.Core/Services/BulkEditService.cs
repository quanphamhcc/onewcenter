using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Opt-in options for a C3 bulk edit. Each <c>Apply*</c> flag gates one batchable field:
/// when the flag is false the corresponding property is left exactly as-is on every item,
/// so bulk edit never wipes settings the user did not intend to change.
/// Plain data (no WPF, no I/O) so it is reachable from the net8.0 test project.
/// </summary>
public class BulkEditOptions
{
    /// <summary>When true, each item's <see cref="ServiceItem.BackgroundType"/> is set to <see cref="BackgroundType"/>.</summary>
    public bool ApplyBackground { get; set; }

    /// <summary>
    /// Background override to apply when <see cref="ApplyBackground"/> is true. A null value means
    /// "no mappable choice" (e.g. the panel's "None" placeholder) and is treated as no change.
    /// </summary>
    public BackgroundType? BackgroundType { get; set; }

    /// <summary>When true, each item's <see cref="ServiceItem.TransitionTimeMs"/> is set to <see cref="TransitionTimeMs"/>.</summary>
    public bool ApplyTransition { get; set; }

    /// <summary>Transition fade time (ms) to apply when <see cref="ApplyTransition"/> is true.</summary>
    public int TransitionTimeMs { get; set; }

    /// <summary>When true, each item's auto-advance after-condition is set to <see cref="AutoAdvanceCondition"/>.</summary>
    public bool ApplyAutoAdvance { get; set; }

    /// <summary>After-condition to apply when <see cref="ApplyAutoAdvance"/> is true.</summary>
    public AutoAdvanceAfterCondition AutoAdvanceCondition { get; set; }
}

/// <summary>
/// Pure, testable implementation of the C3 "apply these chosen fields to this set of items"
/// operation. Lives in Core (not the WPF code-behind) so the test project can exercise it and
/// so the per-item C4 UI can share the same label→enum mapping.
/// </summary>
public static class BulkEditService
{
    /// <summary>
    /// Apply only the opted-in fields of <paramref name="options"/> to every item in
    /// <paramref name="items"/>. Fields whose <c>Apply*</c> flag is false are left untouched.
    /// A no-op options object (all flags false) or an empty selection changes nothing.
    /// Reuses the existing <see cref="ServiceItem"/>/<see cref="ServiceItemSettings"/> models;
    /// no new storage format.
    /// </summary>
    public static void Apply(BulkEditOptions options, IEnumerable<ServiceItem> items)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (items == null) throw new ArgumentNullException(nameof(items));

        foreach (var item in items)
        {
            if (item == null) continue;

            // Background: only touch when opted in AND a real enum value was chosen.
            // A null BackgroundType (panel "None"/unmapped) is treated as "leave as-is".
            if (options.ApplyBackground && options.BackgroundType.HasValue)
            {
                item.BackgroundType = options.BackgroundType.Value;
            }

            if (options.ApplyTransition)
            {
                item.TransitionTimeMs = options.TransitionTimeMs;
            }

            if (options.ApplyAutoAdvance)
            {
                // Lazily create the C4 settings object only when needed, and change ONLY the
                // after-condition — GoTo, delay, fade, and GoToItemId are per-item and preserved.
                item.AutoAdvanceSettings ??= new ServiceItemSettings();
                item.AutoAdvanceSettings.AutoAdvanceAfterCondition = options.AutoAdvanceCondition;
            }
        }
    }

    /// <summary>
    /// Map a background combo label to a <see cref="Models.BackgroundType"/>. There is no
    /// <c>None</c> background enum, so "None", empty, and any unrecognized label map to null
    /// ("leave as-is"), keeping the opt-in semantics intact.
    /// </summary>
    public static BackgroundType? ParseBackground(string? label) => label switch
    {
        "Solid Color" => Models.BackgroundType.SolidColor,
        "Image" => Models.BackgroundType.Image,
        "Video" => Models.BackgroundType.Video,
        "Gradient" => Models.BackgroundType.Gradient,
        _ => null
    };

    /// <summary>
    /// Map an auto-advance combo label to an <see cref="AutoAdvanceAfterCondition"/>. Mirrors the
    /// C4 per-item mapping ("Time Delay"→Timer, "End of Media"→AudioEnd, else Manual) so bulk edit
    /// and per-item edit agree.
    /// </summary>
    public static AutoAdvanceAfterCondition ParseAfterCondition(string? label) => label switch
    {
        "Time Delay" => AutoAdvanceAfterCondition.Timer,
        "End of Media" => AutoAdvanceAfterCondition.AudioEnd,
        _ => AutoAdvanceAfterCondition.Manual
    };
}
