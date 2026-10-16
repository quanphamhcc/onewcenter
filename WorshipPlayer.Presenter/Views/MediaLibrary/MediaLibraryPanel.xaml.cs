using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.MediaLibrary;

/// <summary>
/// Media library panel with grid/list/compact views, search, and type filtering.
/// </summary>
public partial class MediaLibraryPanel : UserControl
{
    private IMediaLibraryService? _mediaLibraryService;
    private string _currentView = "Grid";

    // Unfiltered master list of all loaded/imported items. Filtering composes over this
    // into the bound <see cref="Items"/> collection, so import (which appends here) and
    // load both feed the same source.
    private readonly List<MediaItem> _allItems = new();

    public IMediaLibraryService? MediaLibraryService
    {
        get => _mediaLibraryService;
        set
        {
            _mediaLibraryService = value;
            RefreshMedia();
        }
    }

    public ObservableCollection<MediaItem> Items { get; } = new();

    public event EventHandler<MediaItem>? MediaSelected;
    public event EventHandler? ImportRequested;

    public MediaLibraryPanel()
    {
        InitializeComponent();
        if (ListPanel != null)
            ListPanel.ItemsSource = Items;
        Items.CollectionChanged += (_, _) => UpdateEmptyState();
        Loaded += (_, _) => UpdateEmptyState();
    }

    public void RefreshMedia()
    {
        if (_mediaLibraryService == null)
        {
            UpdateEmptyState();
            return;
        }

        _ = LoadMediaAsync();
    }

    private async Task LoadMediaAsync()
    {
        if (_mediaLibraryService == null) return;

        try
        {
            var allItems = await _mediaLibraryService.GetAllAsync();
            _allItems.Clear();
            foreach (var item in allItems)
            {
                _allItems.Add(new MediaItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    MediaType = item.MediaType.ToString(),
                    FilePath = item.FilePath,
                    FileSize = item.FileSize,
                    DateAdded = item.DateAdded,
                    Tags = item.Tags
                });
            }

            RebuildTagFilterOptions(allItems);
            ApplyFilters();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Media library load failed: {ex.Message}");
            UpdateEmptyState();
        }
    }

    /// <summary>
    /// Appends an imported item to the master list and re-applies the active filters,
    /// so a newly imported file shows up live without a full reload. Called by the
    /// OperatorWindow import handler.
    /// </summary>
    public void AddImportedItem(MediaItem item)
    {
        if (item == null) return;
        _allItems.Add(item);
        RebuildTagFilterOptions(_allItems.Select(ToLibraryItem));
        ApplyFilters();
    }

    /// <summary>
    /// Rebuilds the Tag dropdown from the distinct tags across the supplied items,
    /// preserving the "All Tags" default and the current selection when still present.
    /// </summary>
    private void RebuildTagFilterOptions(IEnumerable<MediaLibraryItem> items)
    {
        if (TagFilterComboBox == null) return;

        var previous = (TagFilterComboBox.SelectedItem as ComboBoxItem)?.Content as string;

        TagFilterComboBox.Items.Clear();
        TagFilterComboBox.Items.Add(new ComboBoxItem { Content = "All Tags" });

        foreach (var tag in MediaLibraryFilter.DistinctTags(items))
            TagFilterComboBox.Items.Add(new ComboBoxItem { Content = tag });

        var restored = TagFilterComboBox.Items
            .OfType<ComboBoxItem>()
            .FirstOrDefault(i => (i.Content as string) == previous);
        TagFilterComboBox.SelectedItem = restored ?? TagFilterComboBox.Items[0];
    }

    private static MediaLibraryItem ToLibraryItem(MediaItem item) => new()
    {
        Id = item.Id,
        Name = item.Name,
        MediaType = Enum.TryParse<MediaLibraryType>(item.MediaType, out var t) ? t : MediaLibraryType.Image,
        FilePath = item.FilePath,
        FileSize = item.FileSize,
        DateAdded = item.DateAdded,
        Tags = item.Tags
    };

    private void Import_Click(object sender, RoutedEventArgs e)
    {
        ImportRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyFilters();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyFilters();
    }

    private void GridView_Click(object sender, RoutedEventArgs e)
    {
        _currentView = "Grid";
        GridPanel.Visibility = Visibility.Visible;
        ListPanel.Visibility = Visibility.Collapsed;
        UpdateEmptyState();
    }

    private void ListView_Click(object sender, RoutedEventArgs e)
    {
        _currentView = "List";
        GridPanel.Visibility = Visibility.Collapsed;
        ListPanel.Visibility = Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmptyState();
    }

    private void CompactView_Click(object sender, RoutedEventArgs e)
    {
        _currentView = "Compact";
        GridPanel.Visibility = Visibility.Collapsed;
        ListPanel.Visibility = Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateEmptyState();
    }

    private void MediaItem_Selected(object sender, SelectionChangedEventArgs e)
    {
        if (ListPanel.SelectedItem is MediaItem item)
        {
            MediaSelected?.Invoke(this, item);
        }
    }

    private void ClearFilters_Click(object sender, RoutedEventArgs e)
    {
        if (TypeFilterComboBox != null) TypeFilterComboBox.SelectedIndex = 0; // All Types
        if (TagFilterComboBox != null && TagFilterComboBox.Items.Count > 0)
            TagFilterComboBox.SelectedIndex = 0; // All Tags
        if (SearchTextBox != null) SearchTextBox.Text = string.Empty;
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        // Read the selected filter dimensions; null/empty each means "no filter".
        MediaLibraryType? type = null;
        if ((TypeFilterComboBox?.SelectedItem as ComboBoxItem)?.Content is string typeText &&
            Enum.TryParse<MediaLibraryType>(typeText, out var parsedType))
        {
            type = parsedType;
        }

        string? tag = (TagFilterComboBox?.SelectedItem as ComboBoxItem)?.Content as string;
        if (tag == "All Tags") tag = null;

        var search = SearchTextBox?.Text;

        // Compose over the master list via the shared Core helper (service only supports
        // one dimension per call, so combined type AND tag AND search is composed here).
        var source = _allItems.Select(ToLibraryItem);
        var filtered = MediaLibraryFilter.Apply(source, type, tag, search);
        var filteredIds = filtered.Select(f => f.Id).ToHashSet();

        Items.Clear();
        foreach (var item in _allItems.Where(i => filteredIds.Contains(i.Id)))
            Items.Add(item);

        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        if (EmptyStateText == null) return;
        bool hasItems = Items.Count > 0;
        EmptyStateText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
    }
}

/// <summary>
/// Display model for a media item in the library.
/// </summary>
public class MediaItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string MediaType { get; set; } = "Image";
    public string FilePath { get; set; } = string.Empty;
    public long FileSize { get; set; }
    public DateTime DateAdded { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? OrganizationId { get; set; }
}
