using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.MediaLibrary;

/// <summary>
/// Panel showing metadata details for a selected media item.
/// Displays name, type, file size, date added, and editable tags.
/// </summary>
public partial class MediaMetadataPanel : UserControl
{
    private IMediaLibraryService? _mediaLibraryService;
    private string _currentMediaId = string.Empty;

    public IMediaLibraryService? MediaLibraryService
    {
        get => _mediaLibraryService;
        set => _mediaLibraryService = value;
    }

    public event EventHandler<MediaTagsUpdate>? TagsSaved;

    public MediaMetadataPanel()
    {
        InitializeComponent();
    }

    public void LoadMedia(MediaItem item)
    {
        _currentMediaId = item.Id;

        NameText.Text = item.Name;
        TypeText.Text = item.MediaType;
        SizeText.Text = FormatFileSize(item.FileSize);
        DateText.Text = item.DateAdded.ToString("yyyy-MM-dd HH:mm");
        TagsTextBox.Text = string.Join(", ", item.Tags);

        EmptyStateText.Visibility = Visibility.Collapsed;
        DetailsPanel.Visibility = Visibility.Visible;
    }

    public void Clear()
    {
        _currentMediaId = string.Empty;
        EmptyStateText.Visibility = Visibility.Visible;
        DetailsPanel.Visibility = Visibility.Collapsed;
    }

    private void SaveTags_Click(object sender, RoutedEventArgs e)
    {
        var tags = TagsTextBox.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();

        TagsSaved?.Invoke(this, new MediaTagsUpdate
        {
            MediaId = _currentMediaId,
            Tags = tags
        });
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / (1024.0 * 1024.0):F1} MB";
        return $"{bytes / (1024.0 * 1024.0 * 1024.0):F1} GB";
    }
}

/// <summary>
/// Represents a tag update for a media item.
/// </summary>
public class MediaTagsUpdate
{
    public string MediaId { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}
