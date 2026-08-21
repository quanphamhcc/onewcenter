using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.MediaLibrary;

/// <summary>
/// Media library panel with grid/list/compact views, search, and type filtering.
/// </summary>
public partial class MediaLibraryPanel : UserControl
{
    private IMediaLibraryService? _mediaLibraryService;
    private string _currentView = "Grid";

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
            Items.Clear();
            foreach (var item in allItems)
            {
                Items.Add(new MediaItem
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
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Media library load failed: {ex.Message}");
        }

        UpdateEmptyState();
    }

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

    private void ApplyFilters()
    {
        // Filter logic will be connected to IMediaLibraryService
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
