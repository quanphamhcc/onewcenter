using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Helpers for comparing/resolving songs by their FolderPath in a way that is robust
/// to path-casing, trailing separators, and relative vs. absolute differences.
///
/// Rationale: on Windows the folder-scan path (from Directory.GetDirectories using the
/// base-path casing) and the registry path (stored verbatim in _song_registry.json)
/// can differ only by drive-letter case or trailing separator. A raw ordinal
/// comparison then treats them as two different songs — causing duplicate listings and
/// failed selection lookups. Normalizing both sides fixes both symptoms.
/// </summary>
public static class SongPathResolver
{
    /// <summary>
    /// Normalize a folder path for comparison: resolve to a full path when possible,
    /// trim trailing directory separators, and keep it as-is otherwise. Comparison is
    /// expected to be done with <see cref="StringComparer.OrdinalIgnoreCase"/>.
    /// </summary>
    public static string Normalize(string? folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath))
            return string.Empty;

        var path = folderPath.Trim();

        try
        {
            // GetFullPath collapses relative segments and unifies separators.
            path = Path.GetFullPath(path);
        }
        catch
        {
            // Not a rooted/valid path (e.g. a bare id) — fall back to the raw value.
        }

        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>
    /// True when two folder paths refer to the same location after normalization.
    /// </summary>
    public static bool PathsEqual(string? a, string? b)
    {
        return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolve the <see cref="Song"/> an id refers to, trying (in order):
    /// 1) normalized FolderPath match (the id is produced from Song.FolderPath),
    /// 2) folder-name match (id may be just the leaf folder name),
    /// 3) title match as a last-resort fallback.
    /// Returns null if nothing matches.
    /// </summary>
    public static Song? Resolve(IEnumerable<Song> songs, string? id, string? title = null)
    {
        if (songs == null) return null;

        var list = songs as IList<Song> ?? songs.ToList();

        if (!string.IsNullOrWhiteSpace(id))
        {
            var normalizedId = Normalize(id);

            var byPath = list.FirstOrDefault(s =>
                string.Equals(Normalize(s.FolderPath), normalizedId, StringComparison.OrdinalIgnoreCase));
            if (byPath != null) return byPath;

            var byFolderName = list.FirstOrDefault(s =>
                !string.IsNullOrEmpty(s.FolderPath) &&
                string.Equals(Path.GetFileName(s.FolderPath.TrimEnd(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                    id, StringComparison.OrdinalIgnoreCase));
            if (byFolderName != null) return byFolderName;
        }

        if (!string.IsNullOrWhiteSpace(title))
        {
            var byTitle = list.FirstOrDefault(s =>
                string.Equals(s.Title, title, StringComparison.OrdinalIgnoreCase));
            if (byTitle != null) return byTitle;
        }

        return null;
    }
}
