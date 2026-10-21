namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure helpers for resolving the AI training-data folder. Kept free of WPF and side effects so
/// the default-path logic is shared between <see cref="WorshipPlayer.Core.Models.AppSettings"/>
/// and the UI, and is unit-testable in the net8.0 Tests project.
/// </summary>
public static class TrainingDataPaths
{
    /// <summary>
    /// Returns the safe default training-data folder under the current user's Documents folder.
    /// This always resolves to an existing, writable per-user location on Windows (unlike a
    /// hardcoded drive that may be absent). The folder itself is NOT created here (pure helper).
    /// </summary>
    public static string DefaultFolder()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "OWC", "TrainingData");

    /// <summary>
    /// True when <paramref name="path"/> is a non-empty path pointing at a directory that exists.
    /// A stale saved path on a missing drive returns false, so the caller treats it as unset.
    /// </summary>
    public static bool IsUsable(string? path)
        => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);
}
