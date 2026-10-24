using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Decides which presenter outputs get an <c>OutputWindow</c> (đợt 2 T16 / R8 — pure
/// Core, no I/O, no WPF). The engine opens exactly one window per planned output; every
/// window shows the SAME content (hard requirement — outputs never diverge), each
/// rendering its foreground with its own resolved template
/// (see <see cref="OutputTemplateResolver"/>).
/// </summary>
public static class OutputWindowPlanner
{
    /// <summary>
    /// One planned window: the output to render plus its validated display index.
    /// </summary>
    /// <param name="Output">The audience output this window renders.</param>
    /// <param name="ClampedDisplayIndex">Validated 0-based display index (never -1).</param>
    public sealed record PlannedOutputWindow(OutputConfig Output, int ClampedDisplayIndex);

    /// <summary>
    /// Returns the audience outputs that should have a window open right now.
    /// An output is planned only when ALL of these hold:
    /// <list type="bullet">
    /// <item><see cref="OutputConfig.Purpose"/> is <see cref="ScreenPurpose.Audience"/>
    /// (Stage outputs keep their existing web-display behavior — never a window);</item>
    /// <item><see cref="OutputConfig.IsEnabled"/> is true (disabling closes the window);</item>
    /// <item>the saved <see cref="OutputConfig.DisplayIndex"/> still maps to a real
    /// monitor (a removed monitor clamps to -1 → no window, same contract as
    /// the pre-T16 single-output path).</item>
    /// </list>
    /// A null output list (should not happen after migration, but tolerated) plans nothing.
    /// </summary>
    /// <param name="outputs">The configured outputs (e.g. <c>PresenterSettings.Outputs</c>).</param>
    /// <param name="monitorCount">Number of currently available monitors.</param>
    public static IReadOnlyList<PlannedOutputWindow> PlanAudienceWindows(
        IReadOnlyList<OutputConfig>? outputs, int monitorCount)
    {
        var result = new List<PlannedOutputWindow>();
        if (outputs == null)
            return result;

        foreach (var output in outputs)
        {
            if (output == null)
                continue;
            if (output.Purpose != ScreenPurpose.Audience)
                continue;
            if (!output.IsEnabled)
                continue;
            var clamped = ClampDisplayIndex(output.DisplayIndex, monitorCount);
            if (clamped < 0)
                continue;
            result.Add(new PlannedOutputWindow(output, clamped));
        }

        return result;
    }

    /// <summary>
    /// Returns the ids of currently-open windows that are NOT in the plan and must be
    /// closed (output disabled, unplugged monitor, removed output, or purpose changed).
    /// Pure helper so the close/open reconciliation is unit-testable.
    /// </summary>
    /// <param name="openWindowIds">Ids (OutputConfig.Id) of currently open windows.</param>
    /// <param name="plan">The current plan from <see cref="PlanAudienceWindows"/>.</param>
    public static IReadOnlyList<string> WindowsToClose(
        IEnumerable<string> openWindowIds,
        IReadOnlyList<PlannedOutputWindow> plan)
    {
        var planned = new HashSet<string>(plan.Select(p => p.Output.Id));
        return openWindowIds.Where(id => !planned.Contains(id)).ToList();
    }

    /// <summary>
    /// Clamp a saved display index to the currently available monitors. Same contract as
    /// the pre-T16 single-output helper (a saved index for a removed monitor, or -1 for
    /// "unassigned", yields -1 = no window).
    /// </summary>
    public static int ClampDisplayIndex(int savedIndex, int monitorCount)
    {
        if (savedIndex < 0) return -1;
        if (monitorCount <= 0) return -1;
        if (savedIndex >= monitorCount) return -1;
        return savedIndex;
    }
}
