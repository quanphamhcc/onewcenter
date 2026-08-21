using WorshipPlayer.Core.Models.Cloud;

namespace WorshipPlayer.Core.Services.Cloud;

/// <summary>
/// How to resolve a sync conflict between local and remote versions.
/// </summary>
public enum ConflictResolution
{
    KeepLocal,
    KeepRemote,
    Merge
}

/// <summary>
/// Event args for sync progress updates, reporting per-song status.
/// </summary>
public class SyncProgressEventArgs : EventArgs
{
    public string SongId { get; set; } = "";
    public int Progress { get; set; }
    public SyncState State { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>
/// Interface for the local sync service that manages synchronization
/// between local song files and the cloud library.
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// Synchronize all tracked songs with the cloud.
    /// </summary>
    Task SyncAllAsync();

    /// <summary>
    /// Synchronize a single song by its ID.
    /// </summary>
    Task SyncSongAsync(string songId);

    /// <summary>
    /// Get the current sync status for all tracked songs.
    /// </summary>
    Task<List<SyncInfo>> GetSyncStatusAsync();

    /// <summary>
    /// Resolve a sync conflict for a specific song.
    /// </summary>
    Task ResolveConflictAsync(string songId, ConflictResolution resolution);

    /// <summary>
    /// Enable or disable automatic background sync.
    /// </summary>
    Task SetAutoSyncAsync(bool enabled);

    /// <summary>
    /// Whether automatic sync is currently enabled.
    /// </summary>
    bool IsAutoSyncEnabled { get; }

    /// <summary>
    /// Fired when sync progress changes for any song.
    /// </summary>
    event EventHandler<SyncProgressEventArgs>? SyncProgressChanged;
}
