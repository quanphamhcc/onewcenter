using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Manages loading, saving, and navigating ServiceOrders.
/// This is a shared service used by both the Backtrack Player and Presenter.
/// Lives in Core so both UI modules can access it.
/// </summary>
/// <remarks>
/// Phase 5 SQL migration: Service Orders now also live in the shared SQLite application
/// database (app.db, <c>ServiceOrders</c> table) via <see cref="AppDatabase"/>, reusing the
/// same helper the media/alert/song-usage stores use (no second helper, no EF; bible.db stays
/// separate). The on-disk JSON files REMAIN authoritative and are still written/read exactly as
/// before — the public API is path-oriented (<see cref="GetFilePathFor"/> /
/// <see cref="LoadAsync(string)"/> / <see cref="SaveAsync(string?)"/> /
/// <see cref="ListServiceFilesAsync"/>) and the UI treats a file path as a service's identity
/// (including an out-of-band <c>File.Delete(path)</c> for deletes). SQLite is kept as a
/// reconciled MIRROR keyed by the normalized file path: <see cref="SaveAsync"/> upserts a row,
/// <see cref="LoadAsync"/> refreshes the loaded order's row, and
/// <see cref="LoadAllAsync"/>/<see cref="ListServiceFilesAsync"/> upsert each scanned file and
/// prune rows whose file no longer exists (absorbing the UI's out-of-band delete). All mirror
/// DB work is wrapped so a DB failure never changes the file-based public behavior or the
/// <see cref="ItemChanged"/>/<see cref="ServiceOrderLoaded"/> events. On first init, if the
/// table is empty and <c>*.json</c> files exist, they are imported once (defensively,
/// non-duplicating, never throwing); the JSON files are LEFT ON DISK. This preserves every
/// observable behavior byte-for-byte while delivering Phase 5's goal of Service Orders in app.db.
/// </remarks>
public class ServiceOrderManager
{
    private readonly string _serviceOrdersBasePath;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly AppDatabase _database;
    private bool _initialized = false;

    private const string CreateSchemaSql = @"
CREATE TABLE IF NOT EXISTS ServiceOrders (
    FilePath        TEXT PRIMARY KEY,
    Id              TEXT NOT NULL DEFAULT '',
    Name            TEXT NOT NULL DEFAULT '',
    Date            TEXT NOT NULL DEFAULT '',
    StartTime       TEXT NULL,
    ServiceType     TEXT NOT NULL DEFAULT '',
    LastWriteUtc    TEXT NOT NULL DEFAULT '',
    DataJson        TEXT NOT NULL
);
";

    public ServiceOrder? CurrentServiceOrder { get; private set; }
    public int CurrentItemIndex { get; private set; } = -1;
    public ServiceItem? CurrentItem => CurrentItemIndex >= 0 && CurrentItemIndex < (CurrentServiceOrder?.Items.Count ?? 0)
        ? CurrentServiceOrder!.Items[CurrentItemIndex]
        : null;

    /// <summary>
    /// Fired when the active service item changes (e.g., moving from one song to the next).
    /// </summary>
    public event EventHandler<ServiceItemChangedEventArgs>? ItemChanged;

    /// <summary>
    /// Fired when a new service order is loaded.
    /// </summary>
    public event EventHandler<ServiceOrder>? ServiceOrderLoaded;

    /// <summary>
    /// Create the manager against the ServiceOrders folder (the on-disk JSON files, which stay
    /// authoritative) and a database path for the SQLite mirror (conceptually
    /// &lt;appFolder&gt;/app.db — the caller passes the path, exactly like
    /// <see cref="MediaLibraryService"/>). When <paramref name="databasePath"/> is null, the
    /// standard app-folder <c>app.db</c> (<see cref="AppDatabase.DefaultPath"/>) is used, so the
    /// existing single-arg call sites keep compiling and behaving unchanged.
    /// </summary>
    public ServiceOrderManager(string serviceOrdersBasePath, string? databasePath = null)
    {
        _serviceOrdersBasePath = serviceOrdersBasePath;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
        _database = new AppDatabase(databasePath ?? AppDatabase.DefaultPath);
    }

    /// <summary>
    /// Load a service order from a JSON file.
    /// </summary>
    public async Task<ServiceOrder?> LoadAsync(string filePath)
    {
        if (!File.Exists(filePath))
            return null;

        try
        {
            var json = await File.ReadAllTextAsync(filePath);
            var serviceOrder = JsonSerializer.Deserialize<ServiceOrder>(json, _jsonOptions);

            if (serviceOrder != null)
            {
                CurrentServiceOrder = serviceOrder;
                CurrentItemIndex = serviceOrder.Items.Count > 0 ? 0 : -1;

                // Keep the SQLite mirror fresh for files edited out-of-band. Best-effort only:
                // a DB failure must never change the file-based load behavior or the event.
                await UpsertRowSafeAsync(serviceOrder, filePath);

                ServiceOrderLoaded?.Invoke(this, serviceOrder);
            }

            return serviceOrder;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading service order: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Save the current service order to a JSON file.
    /// </summary>
    public async Task SaveAsync(string? filePath = null)
    {
        if (CurrentServiceOrder == null) return;

        filePath ??= GetDefaultFilePath(CurrentServiceOrder);
        CurrentServiceOrder.UpdatedAt = DateTime.Now;

        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(CurrentServiceOrder, _jsonOptions);
        await File.WriteAllTextAsync(filePath, json);

        // Mirror the saved order into app.db keyed by the normalized file path (= identity).
        // Best-effort: the JSON file is authoritative and already written above.
        await UpsertRowSafeAsync(CurrentServiceOrder, filePath);
    }

    /// <summary>
    /// Create a new empty service order.
    /// </summary>
    public ServiceOrder CreateNew(string name, string date, string serviceType = "Sunday Morning")
    {
        var order = new ServiceOrder
        {
            Name = name,
            Date = date,
            ServiceType = serviceType
        };

        CurrentServiceOrder = order;
        CurrentItemIndex = -1;
        ServiceOrderLoaded?.Invoke(this, order);
        return order;
    }

    /// <summary>
    /// Add a service item to the current order.
    /// </summary>
    public void AddItem(ServiceItem item)
    {
        if (CurrentServiceOrder == null) return;

        item.Order = CurrentServiceOrder.Items.Count;
        CurrentServiceOrder.Items.Add(item);

        // If this is the first item, set it as current
        if (CurrentItemIndex < 0)
        {
            CurrentItemIndex = 0;
            ItemChanged?.Invoke(this, new ServiceItemChangedEventArgs
            {
                PreviousIndex = -1,
                NewIndex = 0,
                NewItem = item
            });
        }
    }

    /// <summary>
    /// Remove a service item by index.
    /// </summary>
    public void RemoveItem(int index)
    {
        if (CurrentServiceOrder == null || index < 0 || index >= CurrentServiceOrder.Items.Count)
            return;

        var removedId = CurrentServiceOrder.Items[index].Id;
        CurrentServiceOrder.Items.RemoveAt(index);

        // Purge the removed item's id from every group so no group keeps a dangling reference.
        foreach (var group in CurrentServiceOrder.Groups)
            group.Items.RemoveAll(id => id == removedId);

        // Reorder remaining items
        for (int i = index; i < CurrentServiceOrder.Items.Count; i++)
            CurrentServiceOrder.Items[i].Order = i;

        // Adjust current index
        if (CurrentItemIndex >= CurrentServiceOrder.Items.Count)
            CurrentItemIndex = CurrentServiceOrder.Items.Count - 1;
    }

    /// <summary>
    /// Move a service item to a new position (drag-and-drop reorder).
    /// </summary>
    public void MoveItem(int fromIndex, int toIndex)
    {
        if (CurrentServiceOrder == null) return;
        if (fromIndex < 0 || fromIndex >= CurrentServiceOrder.Items.Count) return;
        if (toIndex < 0 || toIndex >= CurrentServiceOrder.Items.Count) return;

        var item = CurrentServiceOrder.Items[fromIndex];
        CurrentServiceOrder.Items.RemoveAt(fromIndex);
        CurrentServiceOrder.Items.Insert(toIndex, item);

        // Update order values
        for (int i = 0; i < CurrentServiceOrder.Items.Count; i++)
            CurrentServiceOrder.Items[i].Order = i;

        // Track current item position
        if (CurrentItemIndex == fromIndex)
            CurrentItemIndex = toIndex;
        else if (fromIndex < CurrentItemIndex && toIndex >= CurrentItemIndex)
            CurrentItemIndex--;
        else if (fromIndex > CurrentItemIndex && toIndex <= CurrentItemIndex)
            CurrentItemIndex++;
    }

    // ─── Service group operations (roadmap C2) ────────────────────
    // Groups own an ordered list of ServiceItem.Id values (ServiceGroup.Items). An item whose
    // Id is in no group's Items list is "ungrouped". There is a single source of truth (the
    // group side); ServiceItem has no group reference. All mutations happen in place on
    // CurrentServiceOrder.Groups / .Items and are persisted by the existing SaveAsync path.

    /// <summary>
    /// Create a named group, appended after any existing groups. Returns the new group,
    /// or null when no service order is loaded.
    /// </summary>
    public ServiceGroup? CreateGroup(string name)
    {
        if (CurrentServiceOrder == null) return null;

        var group = new ServiceGroup
        {
            Name = string.IsNullOrWhiteSpace(name) ? "Untitled Group" : name.Trim(),
            Order = CurrentServiceOrder.Groups.Count
        };
        CurrentServiceOrder.Groups.Add(group);
        return group;
    }

    /// <summary>
    /// Rename an existing group. Returns false when the order is unloaded or the id is unknown.
    /// </summary>
    public bool RenameGroup(string groupId, string newName)
    {
        if (CurrentServiceOrder == null) return false;

        var group = CurrentServiceOrder.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return false;

        group.Name = string.IsNullOrWhiteSpace(newName) ? group.Name : newName.Trim();
        return true;
    }

    /// <summary>
    /// Delete a group WITHOUT deleting its items. The group's item ids are simply dropped from
    /// the group; the underlying ServiceItems stay in ServiceOrder.Items and become ungrouped.
    /// Remaining groups' Order values are compacted. Returns false on unloaded order / unknown id.
    /// </summary>
    public bool DeleteGroup(string groupId)
    {
        if (CurrentServiceOrder == null) return false;

        var group = CurrentServiceOrder.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return false;

        CurrentServiceOrder.Groups.Remove(group);

        // Compact Order so it stays a dense 0..n-1 sequence.
        var ordered = CurrentServiceOrder.Groups.OrderBy(g => g.Order).ToList();
        for (int i = 0; i < ordered.Count; i++)
            ordered[i].Order = i;

        return true;
    }

    /// <summary>
    /// Assign an item to a group (or ungroup it when <paramref name="targetGroupId"/> is null).
    /// The item id is first removed from every group, then appended to the target group's Items.
    /// Returns false when the order is unloaded, the item id is unknown, or the target group id
    /// is non-null but unknown.
    /// </summary>
    public bool AssignItemToGroup(string itemId, string? targetGroupId)
    {
        if (CurrentServiceOrder == null) return false;
        if (!CurrentServiceOrder.Items.Any(i => i.Id == itemId)) return false;

        ServiceGroup? target = null;
        if (targetGroupId != null)
        {
            target = CurrentServiceOrder.Groups.FirstOrDefault(g => g.Id == targetGroupId);
            if (target == null) return false;
        }

        // Remove from any current group (single source of truth).
        foreach (var group in CurrentServiceOrder.Groups)
            group.Items.RemoveAll(id => id == itemId);

        target?.Items.Add(itemId);
        return true;
    }

    /// <summary>
    /// Set a group's collapsed state. Returns false on unloaded order / unknown id.
    /// </summary>
    public bool SetGroupCollapsed(string groupId, bool collapsed)
    {
        if (CurrentServiceOrder == null) return false;

        var group = CurrentServiceOrder.Groups.FirstOrDefault(g => g.Id == groupId);
        if (group == null) return false;

        group.IsCollapsed = collapsed;
        return true;
    }

    /// <summary>
    /// Ids of items not present in any group's Items list, in ServiceOrder.Items order.
    /// </summary>
    public IReadOnlyList<string> GetUngroupedItemIds()
    {
        if (CurrentServiceOrder == null) return Array.Empty<string>();

        var grouped = new HashSet<string>(
            CurrentServiceOrder.Groups.SelectMany(g => g.Items));

        return CurrentServiceOrder.Items
            .Where(i => !grouped.Contains(i.Id))
            .Select(i => i.Id)
            .ToList();
    }

    /// <summary>
    /// Navigate to a specific service item by index.
    /// </summary>
    public void GoToItem(int index)
    {
        if (CurrentServiceOrder == null || index < 0 || index >= CurrentServiceOrder.Items.Count)
            return;

        int previous = CurrentItemIndex;
        CurrentItemIndex = index;

        ItemChanged?.Invoke(this, new ServiceItemChangedEventArgs
        {
            PreviousIndex = previous,
            NewIndex = index,
            NewItem = CurrentServiceOrder.Items[index]
        });
    }

    /// <summary>
    /// Advance to the next service item.
    /// Returns true if advanced, false if already at the end.
    /// </summary>
    public bool NextItem()
    {
        if (CurrentServiceOrder == null) return false;

        int nextIndex = CurrentItemIndex + 1;
        if (nextIndex >= CurrentServiceOrder.Items.Count)
            return false;

        GoToItem(nextIndex);
        return true;
    }

    /// <summary>
    /// Go back to the previous service item.
    /// Returns true if moved back, false if already at the start.
    /// </summary>
    public bool PreviousItem()
    {
        if (CurrentServiceOrder == null) return false;

        int prevIndex = CurrentItemIndex - 1;
        if (prevIndex < 0)
            return false;

        GoToItem(prevIndex);
        return true;
    }

    /// <summary>
    /// Get all song items in the service order (for Backtrack to load).
    /// </summary>
    public List<ServiceItem> GetSongItems()
    {
        return CurrentServiceOrder?.Items
            .Where(i => i.Type == ServiceItemType.Song)
            .ToList() ?? new();
    }

    /// <summary>
    /// Find the next song item after the current position (for Backtrack auto-load).
    /// </summary>
    public ServiceItem? GetNextSongItem()
    {
        if (CurrentServiceOrder == null) return null;

        return CurrentServiceOrder.Items
            .Skip(CurrentItemIndex + 1)
            .FirstOrDefault(i => i.Type == ServiceItemType.Song);
    }

    /// <summary>
    /// Load all service order files from the base path.
    /// </summary>
    public async Task<List<ServiceOrder>> LoadAllAsync()
    {
        var orders = new List<ServiceOrder>();

        if (!Directory.Exists(_serviceOrdersBasePath))
        {
            Directory.CreateDirectory(_serviceOrdersBasePath);
            return orders;
        }

        var scanned = new List<(string path, ServiceOrder order)>();
        foreach (var file in Directory.GetFiles(_serviceOrdersBasePath, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var order = JsonSerializer.Deserialize<ServiceOrder>(json, _jsonOptions);
                if (order != null)
                {
                    orders.Add(order);
                    scanned.Add((file, order));
                }
            }
            catch { }
        }

        // Reconcile the SQLite mirror with the files that are actually on disk (upsert present,
        // prune vanished). Best-effort; the returned list/ordering comes only from the scan above.
        await ReconcileSafeAsync(scanned);

        return orders.OrderByDescending(o => o.Date).ToList();
    }

    /// <summary>
    /// Scan the base path for service-order files, returning one <see cref="ServiceFileInfo"/>
    /// per valid file (path + last-write time + deserialized order). Mirrors
    /// <see cref="LoadAllAsync"/>'s scan: a missing directory is created and yields an empty
    /// list, and corrupt/unreadable files are skipped. Ordering is the builder's job, so the
    /// files are returned in scan order.
    /// </summary>
    public async Task<List<ServiceFileInfo>> ListServiceFilesAsync()
    {
        var files = new List<ServiceFileInfo>();

        if (!Directory.Exists(_serviceOrdersBasePath))
        {
            Directory.CreateDirectory(_serviceOrdersBasePath);
            return files;
        }

        var scanned = new List<(string path, ServiceOrder order)>();
        foreach (var file in Directory.GetFiles(_serviceOrdersBasePath, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var order = JsonSerializer.Deserialize<ServiceOrder>(json, _jsonOptions);
                if (order != null)
                {
                    files.Add(new ServiceFileInfo(file, new FileInfo(file).LastWriteTimeUtc, order));
                    scanned.Add((file, order));
                }
            }
            catch { }
        }

        // Reconcile the SQLite mirror with the on-disk files (upsert present, prune vanished —
        // this absorbs the UI's out-of-band File.Delete). Best-effort; the returned list and its
        // (scan) ordering are untouched so ServiceListingBuilder behavior stays identical.
        await ReconcileSafeAsync(scanned);

        return files;
    }

    /// <summary>
    /// Resolve the file path this manager would use when saving the given order with a
    /// default path. Exposes the private naming logic so the host can detect
    /// save-location changes without duplicating the convention.
    /// </summary>
    public string GetFilePathFor(ServiceOrder order) => GetDefaultFilePath(order);

    /// <summary>
    /// Create a service order from an existing Setlist (migration helper).
    /// Converts the old Setlist format to the new ServiceOrder format.
    /// </summary>
    public ServiceOrder CreateFromSetlist(Setlist setlist, List<Song> songs)
    {
        var order = new ServiceOrder
        {
            Name = setlist.Name,
            Date = setlist.Date
        };

        int orderIndex = 0;
        foreach (var entry in setlist.Entries)
        {
            var song = songs.FirstOrDefault(s =>
                s.FolderPath.EndsWith(entry.SongFolder) ||
                Path.GetFileName(s.FolderPath) == entry.SongFolder);

            order.Items.Add(new ServiceItem
            {
                Type = ServiceItemType.Song,
                Title = song?.Title ?? Path.GetFileName(entry.SongFolder),
                SongFolder = entry.SongFolder,
                KeyShift = entry.KeyShift,
                TempoPercent = entry.TempoPercent,
                UseBacktrack = true,
                Order = orderIndex++
            });
        }

        return order;
    }

    // ─── SQLite mirror (Phase 5) ──────────────────────────────────
    // The on-disk JSON files are authoritative; these helpers keep an app.db mirror in sync.
    // Everything here is best-effort and never allowed to surface an exception into the
    // file-based public behavior or the ItemChanged/ServiceOrderLoaded events.

    /// <summary>
    /// Create the ServiceOrders schema (idempotent) and run the one-time JSON import once per
    /// process via the <see cref="_initialized"/> guard. Mirrors
    /// <see cref="MediaLibraryService"/>'s EnsureInitializedAsync.
    /// </summary>
    private async Task EnsureInitializedAsync()
    {
        if (_initialized) return;

        _database.EnsureDirectory();

        using var connection = _database.CreateConnection();
        await connection.OpenAsync();

        using (var schemaCmd = connection.CreateCommand())
        {
            schemaCmd.CommandText = CreateSchemaSql;
            await schemaCmd.ExecuteNonQueryAsync();
        }

        await ImportFilesIfNeededAsync(connection);

        _initialized = true;
    }

    /// <summary>
    /// Best-effort wrapper around <see cref="UpsertRowAsync"/>: ensures the schema exists then
    /// upserts the mirror row, swallowing any DB failure so the authoritative JSON path is
    /// unaffected.
    /// </summary>
    private async Task UpsertRowSafeAsync(ServiceOrder order, string filePath)
    {
        try
        {
            await EnsureInitializedAsync();
            using var connection = _database.CreateConnection();
            await connection.OpenAsync();
            await UpsertRowAsync(connection, null, order, filePath);
        }
        catch
        {
            // The JSON file is the source of truth and is already written/read; a mirror
            // failure must never break the public behavior.
        }
    }

    /// <summary>
    /// Best-effort reconcile: upsert every scanned (path, order) pair and prune any mirror row
    /// whose FilePath is no longer among the scanned files (absorbs the UI's out-of-band
    /// File.Delete). Swallows any DB failure.
    /// </summary>
    private async Task ReconcileSafeAsync(IReadOnlyList<(string path, ServiceOrder order)> scanned)
    {
        try
        {
            await EnsureInitializedAsync();
            using var connection = _database.CreateConnection();
            await connection.OpenAsync();

            using var transaction = connection.BeginTransaction();

            var keepPaths = new List<string>(scanned.Count);
            foreach (var (path, order) in scanned)
            {
                var normalized = NormalizePath(path);
                keepPaths.Add(normalized);
                await UpsertRowAsync(connection, transaction, order, path);
            }

            // Prune rows whose file is gone. Build a parameterized NOT IN list.
            using (var deleteCmd = connection.CreateCommand())
            {
                deleteCmd.Transaction = transaction;
                if (keepPaths.Count == 0)
                {
                    deleteCmd.CommandText = "DELETE FROM ServiceOrders";
                }
                else
                {
                    var paramNames = new List<string>(keepPaths.Count);
                    for (int i = 0; i < keepPaths.Count; i++)
                    {
                        var p = "@keep" + i;
                        paramNames.Add(p);
                        deleteCmd.Parameters.AddWithValue(p, keepPaths[i]);
                    }
                    deleteCmd.CommandText =
                        $"DELETE FROM ServiceOrders WHERE FilePath NOT IN ({string.Join(",", paramNames)})";
                }
                await deleteCmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            // Files are authoritative; a mirror reconcile failure is non-fatal.
        }
    }

    /// <summary>
    /// Insert or update the mirror row for an order keyed by its normalized file path. Promotes
    /// the fields the listing/sorting consume (Id, Name, Date, StartTime, ServiceType,
    /// LastWriteUtc) and keeps the whole order serialized in DataJson (with the manager's
    /// _jsonOptions) so it round-trips losslessly. Mirrors <see cref="MediaLibraryService"/>'s
    /// upsert shape.
    /// </summary>
    private async Task UpsertRowAsync(SqliteConnection connection, SqliteTransaction? transaction, ServiceOrder order, string filePath)
    {
        var normalized = NormalizePath(filePath);

        using var cmd = connection.CreateCommand();
        if (transaction != null)
            cmd.Transaction = transaction;

        cmd.CommandText = @"
            INSERT INTO ServiceOrders (FilePath, Id, Name, Date, StartTime, ServiceType, LastWriteUtc, DataJson)
            VALUES (@FilePath, @Id, @Name, @Date, @StartTime, @ServiceType, @LastWriteUtc, @DataJson)
            ON CONFLICT(FilePath) DO UPDATE SET
                Id = excluded.Id,
                Name = excluded.Name,
                Date = excluded.Date,
                StartTime = excluded.StartTime,
                ServiceType = excluded.ServiceType,
                LastWriteUtc = excluded.LastWriteUtc,
                DataJson = excluded.DataJson";
        BindOrder(cmd, order, normalized, filePath);
        await cmd.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Bind an order's promoted columns + DataJson. StartTime is stored as an ISO-8601 round-trip
    /// string or NULL (preserving the nullable semantics). LastWriteUtc comes from the file when
    /// it exists else UtcNow.
    /// </summary>
    private void BindOrder(SqliteCommand cmd, ServiceOrder order, string normalizedPath, string filePath)
    {
        cmd.Parameters.AddWithValue("@FilePath", normalizedPath);
        cmd.Parameters.AddWithValue("@Id", order.Id ?? "");
        cmd.Parameters.AddWithValue("@Name", order.Name ?? "");
        cmd.Parameters.AddWithValue("@Date", order.Date ?? "");
        cmd.Parameters.AddWithValue("@StartTime",
            order.StartTime.HasValue
                ? order.StartTime.Value.ToString("o")
                : (object)DBNull.Value);
        cmd.Parameters.AddWithValue("@ServiceType", order.ServiceType ?? "");

        var lastWrite = File.Exists(filePath)
            ? new FileInfo(filePath).LastWriteTimeUtc
            : DateTime.UtcNow;
        cmd.Parameters.AddWithValue("@LastWriteUtc", lastWrite.ToString("o"));

        cmd.Parameters.AddWithValue("@DataJson", JsonSerializer.Serialize(order, _jsonOptions));
    }

    /// <summary>
    /// Defensive one-time import: if the ServiceOrders table is empty AND <c>*.json</c> files
    /// exist under the base path, import each as a mirror row once. Preserves historical ids/dates
    /// (assigns a GUID id only when missing). Tolerates a missing folder, missing/malformed files
    /// (skips the single bad file and continues) — never throws. The empty-table guard makes this
    /// non-duplicating. JSON files are LEFT ON DISK and remain authoritative. Mirrors
    /// <see cref="MediaLibraryService"/>'s ImportSidecarsIfNeededAsync.
    /// </summary>
    private async Task ImportFilesIfNeededAsync(SqliteConnection connection)
    {
        try
        {
            if (!Directory.Exists(_serviceOrdersBasePath))
                return;

            using (var countCmd = connection.CreateCommand())
            {
                countCmd.CommandText = "SELECT COUNT(*) FROM ServiceOrders";
                var count = Convert.ToInt64(await countCmd.ExecuteScalarAsync());
                if (count > 0)
                    return;
            }

            var jsonFiles = Directory.GetFiles(_serviceOrdersBasePath, "*.json");
            if (jsonFiles.Length == 0)
                return;

            using var transaction = connection.BeginTransaction();
            foreach (var file in jsonFiles)
            {
                ServiceOrder? order;
                try
                {
                    var json = await File.ReadAllTextAsync(file);
                    order = JsonSerializer.Deserialize<ServiceOrder>(json, _jsonOptions);
                }
                catch
                {
                    // Skip a single malformed file and continue with the rest.
                    continue;
                }

                if (order == null)
                    continue;

                if (string.IsNullOrEmpty(order.Id))
                    order.Id = Guid.NewGuid().ToString();

                var normalized = NormalizePath(file);
                using var insertCmd = connection.CreateCommand();
                insertCmd.Transaction = transaction;
                insertCmd.CommandText = @"
                    INSERT OR IGNORE INTO ServiceOrders (FilePath, Id, Name, Date, StartTime, ServiceType, LastWriteUtc, DataJson)
                    VALUES (@FilePath, @Id, @Name, @Date, @StartTime, @ServiceType, @LastWriteUtc, @DataJson)";
                BindOrder(insertCmd, order, normalized, file);
                await insertCmd.ExecuteNonQueryAsync();
            }

            transaction.Commit();
        }
        catch
        {
            // A malformed/locked folder or file must never throw — leave the table as-is.
        }
    }

    /// <summary>
    /// Normalize a file path to its full, canonical form so the mirror key matches the UI's
    /// notion of a service's identity regardless of how the path was passed in.
    /// </summary>
    private static string NormalizePath(string filePath)
    {
        try
        {
            return Path.GetFullPath(filePath);
        }
        catch
        {
            return filePath;
        }
    }

    private string GetDefaultFilePath(ServiceOrder order)
    {
        var fileName = $"{order.Date}_{SanitizeFileName(order.Name)}.json";
        return Path.Combine(_serviceOrdersBasePath, fileName);
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray());
    }
}

/// <summary>
/// Event args for service item navigation.
/// </summary>
public class ServiceItemChangedEventArgs : EventArgs
{
    public int PreviousIndex { get; set; }
    public int NewIndex { get; set; }
    public ServiceItem? NewItem { get; set; }
}
