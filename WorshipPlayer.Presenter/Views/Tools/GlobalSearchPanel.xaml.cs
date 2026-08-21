using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Tools;

/// <summary>
/// Global search panel that searches across songs, services, and slides.
/// Results are grouped by type with edit/delete actions.
/// </summary>
public partial class GlobalSearchPanel : UserControl
{
    private ISearchService? _searchService;

    public ISearchService? SearchService
    {
        get => _searchService;
        set => _searchService = value;
    }

    public ObservableCollection<SearchResultItem> SongItems { get; } = new();
    public ObservableCollection<SearchResultItem> ServiceItems { get; } = new();
    public ObservableCollection<SearchResultItem> SlideItems { get; } = new();

    public event EventHandler<SearchResultItem>? EditRequested;
    public event EventHandler<SearchResultItem>? DeleteRequested;

    public GlobalSearchPanel()
    {
        InitializeComponent();
        SongsResults.ItemsSource = SongItems;
        ServicesResults.ItemsSource = ServiceItems;
        SlidesResults.ItemsSource = SlideItems;
    }

    private async void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var query = SearchTextBox.Text.Trim();

        if (string.IsNullOrEmpty(query))
        {
            ClearResults();
            EmptyStateText.Visibility = Visibility.Visible;
            NoResultsText.Visibility = Visibility.Collapsed;
            return;
        }

        EmptyStateText.Visibility = Visibility.Collapsed;

        if (_searchService != null)
        {
            var results = await _searchService.SearchAllAsync(query);

            SongItems.Clear();
            foreach (var song in results.Songs)
            {
                SongItems.Add(new SearchResultItem
                {
                    Id = song.Id,
                    Title = song.Title,
                    Subtitle = song.Artist,
                    Type = "Song"
                });
            }

            ServiceItems.Clear();
            foreach (var svc in results.Services)
            {
                ServiceItems.Add(new SearchResultItem
                {
                    Id = svc.Id,
                    Title = svc.Name,
                    Subtitle = svc.Date,
                    Type = "Service"
                });
            }

            SlideItems.Clear();
            foreach (var slide in results.Slides)
            {
                var text = slide.Content.Text;
                SlideItems.Add(new SearchResultItem
                {
                    Id = slide.Id,
                    Title = text.Length > 50
                        ? text.Substring(0, 50) + "..."
                        : text,
                    Subtitle = slide.Type.ToString(),
                    Type = "Slide"
                });
            }

            UpdateSectionVisibility();

            if (results.TotalCount == 0)
            {
                NoResultsText.Visibility = Visibility.Visible;
            }
            else
            {
                NoResultsText.Visibility = Visibility.Collapsed;
            }
        }
    }

    private void EditResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SearchResultItem item)
        {
            EditRequested?.Invoke(this, item);
        }
    }

    private void DeleteResult_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is SearchResultItem item)
        {
            DeleteRequested?.Invoke(this, item);
        }
    }

    private void ClearResults()
    {
        SongItems.Clear();
        ServiceItems.Clear();
        SlideItems.Clear();
        SongsSection.Visibility = Visibility.Collapsed;
        ServicesSection.Visibility = Visibility.Collapsed;
        SlidesSection.Visibility = Visibility.Collapsed;
    }

    private void UpdateSectionVisibility()
    {
        SongsSection.Visibility = SongItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ServicesSection.Visibility = ServiceItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        SlidesSection.Visibility = SlideItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>
/// Display model for search results.
/// </summary>
public class SearchResultItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Subtitle { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
}
