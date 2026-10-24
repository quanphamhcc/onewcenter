using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Resolves the effective foreground template for a (presenter output, service item) pair
/// (đợt 2 T15 / R8 — pure Core, no I/O).
///
/// Resolution order (R8.2):
/// 1. The output's own <see cref="OutputConfig.ForegroundTemplateIdOverride"/>
///    (Audience outputs only);
/// 2. The item's own foreground template id (the caller supplies it — e.g. the
///    <c>TemplateId</c> of the song section being shown, or
///    <see cref="ServiceOrder.StageTemplateId"/> for a Stage output);
/// 3. The default template id supplied by the caller.
/// A null/whitespace id at any layer counts as "not set" and falls through.
///
/// Stage outputs: the per-output foreground override does NOT apply — a stage display has
/// its own foreground path (see <c>StageDisplayForeground</c>), so the caller passes the
/// item's <see cref="ServiceOrder.StageTemplateId"/> as <paramref name="itemTemplateId"/>.
/// This keeps the pre-T15 stage behavior exactly as it was: stage template when the item
/// sets one, otherwise the default.
/// </summary>
public static class OutputTemplateResolver
{
    /// <summary>
    /// Returns the effective foreground template id for the given output/item pair, or
    /// null when no layer provides one.
    /// </summary>
    /// <param name="output">The output being rendered. Must not be null.</param>
    /// <param name="itemTemplateId">
    /// The item's own template id (e.g. the song section's <c>TemplateId</c> for an
    /// Audience output, or the item's <c>StageTemplateId</c> for a Stage output).
    /// Null/empty = the item sets none.</param>
    /// <param name="defaultTemplateId">The service/library default template id. May be null.</param>
    public static string? Resolve(OutputConfig output, string? itemTemplateId, string? defaultTemplateId)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (output.Purpose == ScreenPurpose.Audience &&
            !string.IsNullOrWhiteSpace(output.ForegroundTemplateIdOverride))
        {
            return output.ForegroundTemplateIdOverride;
        }

        if (!string.IsNullOrWhiteSpace(itemTemplateId))
            return itemTemplateId;

        return defaultTemplateId;
    }
}
