using System;
using System.IO;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// TEMPORARY diagnostic file logger (lyric auto-advance hang investigation, Issue #5).
/// Writes timestamped lines to %USERPROFILE%\Documents\OWC\lyric-diag.log. Best-effort: all
/// failures swallowed so logging never affects behavior. REMOVE once diagnosed.
/// </summary>
public static class DiagLog
{
    private static readonly object _lock = new();

    private static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "OWC", "lyric-diag.log");

    public static void Write(string message)
    {
        try
        {
            var path = LogPath;
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            lock (_lock)
            {
                File.AppendAllText(path, $"{DateTime.Now:HH:mm:ss.fff}  {message}{Environment.NewLine}");
            }
        }
        catch { }
    }
}
