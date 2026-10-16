using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models.Cloud;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.SongLibrary;

/// <summary>
/// Song library browser panel with search, filter, and sort capabilities.
/// </summary>
public partial class SongLibraryPanel : UserControl
{
    private ISongLibraryService? _songLibraryService;

    public ISongLibraryService? SongLibraryService
    {
        get => _songLibraryService;
        set
        {
            _songLibraryService = value;
            RefreshSongs();
        }
    }

    public ObservableCollection<SongListItem> Songs { get; } = new();

    public event EventHandler<SongListItem>? SongSelected;

    /// <summary>
    /// Raised when the user clicks "Import ChordPro…". The host (OperatorWindow) owns the
    /// dialog + service logic, keeping this panel service-agnostic — mirrors MediaLibraryPanel.ImportRequested.
    /// </summary>
    public event EventHandler? ImportChordProRequested;

    /// <summary>
    /// Raised when the user clicks "Edit Metadata…". The host (OperatorWindow) reads
    /// <see cref="SelectedSong"/>, opens the editor, and owns the service/save logic —
    /// keeping this panel service-agnostic, mirroring <see cref="ImportChordProRequested"/>.
    /// </summary>
    public event EventHandler? EditMetadataRequested;

    /// <summary>
    /// The currently selected song in the list, or null if none is selected.
    /// Lets the host read the selection without the panel owning service logic.
    /// </summary>
    public SongListItem? SelectedSong => SongListBox?.SelectedItem as SongListItem;

    public SongLibraryPanel()
    {
        InitializeComponent();
        if (SongListBox != null)
            SongListBox.ItemsSource = Songs;
        Songs.CollectionChanged += (_, _) => UpdateEmptyState();
        Loaded += (_, _) => UpdateEmptyState();
    }

    public void RefreshSongs()
    {
        if (_songLibraryService == null)
        {
            UpdateEmptyState();
            return;
        }

        _ = LoadSongsAsync();
    }

    private async Task LoadSongsAsync()
    {
        if (_songLibraryService == null) return;

        try
        {
            var items = await _songLibraryService.GetAllAsync();
            Songs.Clear();
            foreach (var item in items)
            {
                Songs.Add(new SongListItem
                {
                    Id = item.Id,
                    Title = item.Title,
                    Artist = item.Artist,
                    Key = item.Key,
                    DateAdded = item.UpdatedAt
                });
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Song library load failed: {ex.Message}");
        }

        UpdateEmptyState();
    }

    private void ImportChordPro_Click(object sender, RoutedEventArgs e)
    {
        ImportChordProRequested?.Invoke(this, EventArgs.Empty);
    }

    private void EditMetadata_Click(object sender, RoutedEventArgs e)
    {
        EditMetadataRequested?.Invoke(this, EventArgs.Empty);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyFilters();
    }

    private void Filter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        ApplyFilters();
    }

    private void SortAZ_Click(object sender, RoutedEventArgs e)
    {
        var sorted = Songs.OrderBy(s => s.Title).ToList();
        Songs.Clear();
        foreach (var song in sorted)
            Songs.Add(song);
    }

    private void SortDate_Click(object sender, RoutedEventArgs e)
    {
        var sorted = Songs.OrderByDescending(s => s.DateAdded).ToList();
        Songs.Clear();
        foreach (var song in sorted)
            Songs.Add(song);
    }

    private void SongList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SongListBox.SelectedItem is SongListItem song)
        {
            SongSelected?.Invoke(this, song);
        }
    }

    private void ApplyFilters()
    {
        // Filter logic will query ISongLibraryService with current filter state
        UpdateEmptyState();
    }

    private void UpdateEmptyState()
    {
        if (EmptyStateText == null || SongListBox == null) return;
        bool hasSongs = Songs.Count > 0;
        EmptyStateText.Visibility = hasSongs ? Visibility.Collapsed : Visibility.Visible;
        SongListBox.Visibility = hasSongs ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>
/// Display model for a song in the library list.
/// </summary>
public class SongListItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Artist { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public DateTime DateAdded { get; set; }
    public string? OrganizationId { get; set; }
}
