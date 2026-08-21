using WorshipPlayer.Core.Models.Cloud;

namespace WorshipPlayer.Core.Services.Cloud;

/// <summary>
/// Desktop implementation of ISyncService that tracks local song state,
/// detects modifications, and provides the sync workflow structure.
/// Does not make actual HTTP calls (no backend yet), but implements
/// the full workflow logic for future integration.
/// </summary>
public class DesktopSyncService : ISyncService
{
    private readonly string _localSongsFolder;
    private readonly Dictionary<string, SyncInfo> _trackedSongs = new();
    private readonly Dictionary<string, string> _songPaths = new();
    private bool _autoSyncEnabled;

    public bool IsAutoSyncEnabled => _autoSyncEnabled;

    public event EventHandler<SyncProgressEventArgs>? SyncProgressChanged;

    public DesktopSyncService(string localSongsFolder)
    {
        _localSongsFolder = localSongsFolder;
    }

    /// <summary>
    /// Register a song for sync tracking.
    /// </summary>
    public void RegisterSong(string songId, string localPath, int version)
    {
        var syncInfo = new SyncInfo
        {
            SongId = songId,
            LocalVersion = version,
            RemoteVersion = version,
            State = SyncState.UpToDate,
            LastSyncTime = DateTime.UtcNow
        };

        _trackedSongs[songId] = syncInfo;
        _songPaths[songId] = localPath;
    }

    /// <summary>
    /// Scan local song folders and detect which songs have been modified
    /// since their last sync time by comparing file modification timestamps.
    /// </summary>
    public void DetectLocalChanges()
    {
        foreach (var (songId, syncInfo) in _trackedSongs)
        {
            if (!_songPaths.TryGetValue(songId, out var localPath))
                continue;

            if (!Directory.Exists(localPath))
                continue;

            var lastSyncTime = syncInfo.LastSyncTime ?? DateTime.MinValue;
            var files = Directory.GetFiles(localPath, "*", SearchOption.AllDirectories);

            foreach (var file in files)
            {
                var lastWrite = File.GetLastWriteTimeUtc(file);
                if (lastWrite > lastSyncTime)
                {
                    syncInfo.State = SyncState.LocalChanges;
                    break;
                }
            }
        }
    }

    public Task SyncAllAsync()
    {
        foreach (var (songId, syncInfo) in _trackedSongs)
        {
            if (syncInfo.State == SyncState.UpToDate)
                continue;

            // Determine sync direction based on state
            if (syncInfo.State == SyncState.LocalChanges)
            {
                syncInfo.State = SyncState.Uploading;
                OnSyncProgressChanged(songId, 50, SyncState.Uploading, "Uploading local changes...");

                // Stub: actual upload would happen here
                syncInfo.LocalVersion++;
                syncInfo.RemoteVersion = syncInfo.LocalVersion;
            }
            else if (syncInfo.State == SyncState.RemoteChanges)
            {
                syncInfo.State = SyncState.Downloading;
                OnSyncProgressChanged(songId, 50, SyncState.Downloading, "Downloading remote changes...");

                // Stub: actual download would happen here
                syncInfo.LocalVersion = syncInfo.RemoteVersion;
            }

            // Mark as synced
            syncInfo.State = SyncState.UpToDate;
            syncInfo.LastSyncTime = DateTime.UtcNow;
            OnSyncProgressChanged(songId, 100, SyncState.UpToDate, "Sync complete");
        }

        return Task.CompletedTask;
    }

    public Task SyncSongAsync(string songId)
    {
        if (!_trackedSongs.TryGetValue(songId, out var syncInfo))
            return Task.CompletedTask;

        if (syncInfo.State == SyncState.LocalChanges)
        {
            syncInfo.State = SyncState.Uploading;
            OnSyncProgressChanged(songId, 50, SyncState.Uploading, "Uploading local changes...");

            // Stub: actual upload would happen here
            syncInfo.LocalVersion++;
            syncInfo.RemoteVersion = syncInfo.LocalVersion;
        }
        else if (syncInfo.State == SyncState.RemoteChanges)
        {
            syncInfo.State = SyncState.Downloading;
            OnSyncProgressChanged(songId, 50, SyncState.Downloading, "Downloading remote changes...");

            // Stub: actual download would happen here
            syncInfo.LocalVersion = syncInfo.RemoteVersion;
        }

        syncInfo.State = SyncState.UpToDate;
        syncInfo.LastSyncTime = DateTime.UtcNow;
        OnSyncProgressChanged(songId, 100, SyncState.UpToDate, "Sync complete");

        return Task.CompletedTask;
    }

    public Task<List<SyncInfo>> GetSyncStatusAsync()
    {
        return Task.FromResult(_trackedSongs.Values.ToList());
    }

    public Task ResolveConflictAsync(string songId, ConflictResolution resolution)
    {
        if (!_trackedSongs.TryGetValue(songId, out var syncInfo))
            return Task.CompletedTask;

        switch (resolution)
        {
            case ConflictResolution.KeepLocal:
                syncInfo.RemoteVersion = syncInfo.LocalVersion;
                break;
            case ConflictResolution.KeepRemote:
                syncInfo.LocalVersion = syncInfo.RemoteVersion;
                break;
            case ConflictResolution.Merge:
                // Stub: merge logic would produce a new version
                var mergedVersion = Math.Max(syncInfo.LocalVersion, syncInfo.RemoteVersion) + 1;
                syncInfo.LocalVersion = mergedVersion;
                syncInfo.RemoteVersion = mergedVersion;
                break;
        }

        syncInfo.State = SyncState.UpToDate;
        syncInfo.ConflictDetails = null;
        syncInfo.LastSyncTime = DateTime.UtcNow;

        return Task.CompletedTask;
    }

    public Task SetAutoSyncAsync(bool enabled)
    {
        _autoSyncEnabled = enabled;
        return Task.CompletedTask;
    }

    private void OnSyncProgressChanged(string songId, int progress, SyncState state, string message)
    {
        SyncProgressChanged?.Invoke(this, new SyncProgressEventArgs
        {
            SongId = songId,
            Progress = progress,
            State = state,
            Message = message
        });
    }
}
