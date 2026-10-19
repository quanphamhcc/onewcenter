using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Slides;

/// <summary>
/// Slides library browser panel: a searchable, sortable card grid of
/// <see cref="Core.Models.SlidePresentation"/>s with a blue "+ Add Slides" CTA and a per-card ⋮
/// overflow menu (Add to current service / Edit / Duplicate / Delete). Mirrors
/// <see cref="SongLibrary.SongLibraryPanel"/>: the panel is a thin renderer that calls the Core
/// service for the functional actions (Add/Duplicate/Delete) and raises events to the host
/// (OperatorWindow) for the deferred ones (Edit → Stage 2, Add-to-service → Stage 3).
/// </summary>
public partial class SlidesLibraryPanel : UserControl
{
    private ISlidePresentationService? _slidePresentationService;

    // Full (unfiltered) loaded list cached so Search can filter in memory into Presentations
    // without re-querying the DB. (SongLibraryPanel.ApplyFilters is a no-op stub, so we do NOT
    // copy it — we filter from this cache instead.)
    private readonly List<SlidePresentationListItem> _allPresentations = new();

    public ISlidePresentationService? SlidePresentationService
    {
        get => _slidePresentationService;
        set
        {
            _slidePresentationService = value;
            Refresh();
        }
    }

    public ObservableCollection<SlidePresentationListItem> Presentations { get; } = new();

    /// <summary>
    /// Raised when the user picks ⋮ → "Edit". STAGE 2 STUB: the host shows a "coming soon" notice;
    /// the slide editor is not built in Stage 1.
    /// </summary>
    public event EventHandler<SlidePresentationListItem>? EditPresentationRequested;

    /// <summary>
    /// Raised when the user picks ⋮ → "Add to current service". STAGE 3 STUB: the host shows a
    /// "coming soon" notice; adding a presentation to a service order is not built in Stage 1.
    /// </summary>
    public event EventHandler<SlidePresentationListItem>? AddPresentationToServiceRequested;

    public SlidesLibraryPanel()
    {
        InitializeComponent();
        if (PresentationsItemsControl != null)
            PresentationsItemsControl.ItemsSource = Presentations;
        Presentations.CollectionChanged += (_, _) => UpdateEmptyState();
        Loaded += (_, _) => UpdateEmptyState();
    }

    public void Refresh()
    {
        if (_slidePresentationService == null)
        {
            UpdateEmptyState();
            return;
        }

        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        if (_slidePresentationService == null) return;

        try
        {
            var items = await _slidePresentationService.GetAllAsync();
            var now = DateTime.UtcNow;

            _allPresentations.Clear();
            foreach (var p in items)
            {
                _allPresentations.Add(new SlidePresentationListItem
                {
                    Id = p.Id,
                    Name = p.Name,
                    SlideCount = p.Slides?.Count ?? 0,
                    UpdatedAt = p.UpdatedAt,
                    RelativeModified = RelativeTimeFormatter.Format(p.UpdatedAt, now)
                });
            }

            Presentations.Clear();
            foreach (var item in _allPresentations)
                Presentations.Add(item);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Slides library load failed: {ex.Message}");
        }

        UpdateEmptyState();
    }

    // --- Functional Stage-1 actions ---

    /// <summary>
    /// + Add Slides: prompt for a name (default "New Presentation") and create an empty
    /// presentation, then refresh. Functional in Stage 1.
    /// </summary>
    private async void AddSlides_Click(object sender, RoutedEventArgs e)
    {
        if (_slidePresentationService == null) return;

        var dialog = new SlideNameDialog { Owner = Window.GetWindow(this) };
        if (dialog.ShowDialog() != true)
            return;

        try
        {
            await _slidePresentationService.CreateAsync(dialog.PresentationName);
            Refresh();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Add slides failed: {ex.Message}");
        }
    }

    /// <summary>Opens the per-card ⋮ overflow menu (mirrors SongLibraryPanel.Overflow_Click).</summary>
    private void Overflow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    /// <summary>⋮ → Duplicate: deep-clone via the service, then refresh. Functional in Stage 1.</summary>
    private async void DuplicateMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_slidePresentationService == null) return;
        if (((FrameworkElement)sender).Tag is not SlidePresentationListItem item) return;

        try
        {
            await _slidePresentationService.DuplicateAsync(item.Id);
            Refresh();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Duplicate slides failed: {ex.Message}");
        }
    }

    /// <summary>⋮ → Delete: confirm, then delete via the service and refresh. Functional in Stage 1.</summary>
    private async void DeleteMenu_Click(object sender, RoutedEventArgs e)
    {
        if (_slidePresentationService == null) return;
        if (((FrameworkElement)sender).Tag is not SlidePresentationListItem item) return;

        var result = MessageBox.Show(
            Window.GetWindow(this),
            $"Delete the presentation \"{item.Name}\"? This cannot be undone.",
            "Delete Slides",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _slidePresentationService.DeleteAsync(item.Id);
            Refresh();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Delete slides failed: {ex.Message}");
        }
    }

    // --- Stub-raising actions (deferred to Stage 2/3) ---

    /// <summary>⋮ → Edit: raise the event; the host shows a Stage-2 "coming soon" notice.</summary>
    private void EditMenu_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SlidePresentationListItem item)
            EditPresentationRequested?.Invoke(this, item);
    }

    /// <summary>⋮ → Add to current service: raise the event; the host shows a Stage-3 notice.</summary>
    private void AddToServiceMenu_Click(object sender, RoutedEventArgs e)
    {
        if (((FrameworkElement)sender).Tag is SlidePresentationListItem item)
            AddPresentationToServiceRequested?.Invoke(this, item);
    }

    // --- Search + sort (functional, in-memory over the cached full list) ---

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;

        var query = SearchTextBox.Text?.Trim() ?? "";

        Presentations.Clear();
        foreach (var item in _allPresentations)
        {
            if (query.Length == 0 ||
                item.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                Presentations.Add(item);
            }
        }

        UpdateEmptyState();
    }

    private void SortAZ_Click(object sender, RoutedEventArgs e)
    {
        var sorted = Presentations.OrderBy(p => p.Name).ToList();
        Presentations.Clear();
        foreach (var item in sorted)
            Presentations.Add(item);
    }

    private void SortDate_Click(object sender, RoutedEventArgs e)
    {
        var sorted = Presentations.OrderByDescending(p => p.UpdatedAt).ToList();
        Presentations.Clear();
        foreach (var item in sorted)
            Presentations.Add(item);
    }

    private void UpdateEmptyState()
    {
        if (EmptyStateText == null || PresentationsItemsControl == null) return;
        bool hasItems = Presentations.Count > 0;
        EmptyStateText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        PresentationsItemsControl.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>
/// Display model for a presentation card in the Slides library (mirrors
/// <see cref="SongLibrary.SongListItem"/>).
/// </summary>
public class SlidePresentationListItem
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int SlideCount { get; set; }

    /// <summary>UTC "last modified" from the row, used for the Date sort.</summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>Relative "modified" text (RelativeTimeFormatter.Format(UpdatedAt, UtcNow)).</summary>
    public string RelativeModified { get; set; } = "";

    public string SubLine => $"{SlideCount} slide{(SlideCount == 1 ? "" : "s")} · Modified {RelativeModified}";
}
