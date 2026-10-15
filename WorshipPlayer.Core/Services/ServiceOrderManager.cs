using System.Text.Json;
using System.Text.Json.Serialization;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Core.Services;

/// <summary>
/// Manages loading, saving, and navigating ServiceOrders.
/// This is a shared service used by both the Backtrack Player and Presenter.
/// Lives in Core so both UI modules can access it.
/// </summary>
public class ServiceOrderManager
{
    private readonly string _serviceOrdersBasePath;
    private readonly JsonSerializerOptions _jsonOptions;

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

    public ServiceOrderManager(string serviceOrdersBasePath)
    {
        _serviceOrdersBasePath = serviceOrdersBasePath;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() }
        };
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

        CurrentServiceOrder.Items.RemoveAt(index);

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

        foreach (var file in Directory.GetFiles(_serviceOrdersBasePath, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var order = JsonSerializer.Deserialize<ServiceOrder>(json, _jsonOptions);
                if (order != null)
                    orders.Add(order);
            }
            catch { }
        }

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

        foreach (var file in Directory.GetFiles(_serviceOrdersBasePath, "*.json"))
        {
            try
            {
                var json = await File.ReadAllTextAsync(file);
                var order = JsonSerializer.Deserialize<ServiceOrder>(json, _jsonOptions);
                if (order != null)
                    files.Add(new ServiceFileInfo(file, new FileInfo(file).LastWriteTimeUtc, order));
            }
            catch { }
        }

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
