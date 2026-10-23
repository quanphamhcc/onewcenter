using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// One-way migration of the legacy fixed display indices
/// (<see cref="PresenterSettings.OutputDisplay"/> / <see cref="PresenterSettings.StageDisplay"/>)
/// into the T15 output list (<see cref="PresenterSettings.Outputs"/>).
/// Pure, idempotent and additive: it only acts when <c>Outputs</c> is null (i.e. the
/// "outputs" JSON key was absent — an older settings payload). A present list is the
/// user's own state and is returned untouched, even when empty.
/// </summary>
public static class OutputMigration
{
    /// <summary>
    /// Stable id assigned to the migrated main-audience output, so repeated loads of the
    /// same legacy settings always produce the same list (no id churn).
    /// </summary>
    public const string MainAudienceOutputId = "output-main-audience";

    /// <summary>
    /// Stable id assigned to the migrated stage output.
    /// </summary>
    public const string StageOutputId = "output-stage";

    /// <summary>
    /// Ensures <paramref name="settings"/> carries an output list. When the list is null,
    /// synthesizes it from the legacy indices: one Audience output named "Main Audience"
    /// for a configured <c>OutputDisplay</c> (&gt;= 0), and one Stage output named
    /// "Stage Display" for a configured <c>StageDisplay</c> (&gt;= 0). Unconfigured
    /// indices (-1) produce no output. Returns the effective list.
    /// </summary>
    public static List<OutputConfig> EnsureOutputs(PresenterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (settings.Outputs != null)
            return settings.Outputs;

        var outputs = new List<OutputConfig>();

        if (settings.OutputDisplay >= 0)
        {
            outputs.Add(new OutputConfig
            {
                Id = MainAudienceOutputId,
                Name = "Main Audience",
                DisplayIndex = settings.OutputDisplay,
                Purpose = ScreenPurpose.Audience
            });
        }

        if (settings.StageDisplay >= 0)
        {
            outputs.Add(new OutputConfig
            {
                Id = StageOutputId,
                Name = "Stage Display",
                DisplayIndex = settings.StageDisplay,
                Purpose = ScreenPurpose.Stage
            });
        }

        settings.Outputs = outputs;
        return outputs;
    }
}
