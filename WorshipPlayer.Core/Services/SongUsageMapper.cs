using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Pure mapping between a <see cref="Song"/> and a <see cref="SongUsageRecord"/> for CCLI reporting.
/// Lives in Core so the log-site mapping (used by the WPF-only PresenterIntegration) can be unit-tested
/// without referencing the Presenter/UI projects.
/// </summary>
public static class SongUsageMapper
{
    /// <summary>
    /// Build a <see cref="SongUsageRecord"/> from a played <see cref="Song"/> and the service context.
    /// Captures the song's Artist and CCLI number so the CCLI report can group/aggregate on them.
    /// </summary>
    /// <remarks>
    /// Does NOT set <see cref="SongUsageRecord.DatePlayed"/> — <see cref="SongUsageService.LogUsageAsync(SongUsageRecord)"/>
    /// stamps that at UtcNow. Leaves <see cref="SongUsageRecord.Id"/> empty so the service assigns a GUID.
    /// Maps <see cref="SongUsageRecord.SongId"/> to <see cref="Song.FolderPath"/>, matching the existing log site.
    /// </remarks>
    public static SongUsageRecord FromSong(Song song, string serviceName, DateTime serviceDate)
    {
        ArgumentNullException.ThrowIfNull(song);

        return new SongUsageRecord
        {
            Id = string.Empty,
            SongTitle = song.Title,
            SongId = song.FolderPath,
            Artist = song.Artist,
            CcliNumber = song.Ccli,
            ServiceName = serviceName,
            ServiceDate = serviceDate
        };
    }
}
