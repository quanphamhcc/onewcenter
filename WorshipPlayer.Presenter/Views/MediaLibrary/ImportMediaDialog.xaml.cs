using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using Microsoft.Win32;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.MediaLibrary;

/// <summary>
/// Dialog for importing media files (images and videos) into the media library.
/// Supports multi-select with progress display.
/// </summary>
public partial class ImportMediaDialog : Window
{
    private IMediaLibraryService? _mediaLibraryService;

    public IMediaLibraryService? MediaLibraryService
    {
        get => _mediaLibraryService;
        set => _mediaLibraryService = value;
    }

    public ObservableCollection<ImportFileItem> Files { get; } = new();
    public List<string> ImportedPaths { get; } = new();

    public ImportMediaDialog()
    {
        InitializeComponent();
        FilesListBox.ItemsSource = Files;
    }

    private void BrowseFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Media Files",
            Multiselect = true,
            Filter = "Media Files (*.png;*.jpg;*.jpeg;*.gif;*.webp;*.mp4;*.mov;*.webm;*.avi)|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.mp4;*.mov;*.webm;*.avi|Images (*.png;*.jpg;*.jpeg;*.gif;*.webp)|*.png;*.jpg;*.jpeg;*.gif;*.webp|Videos (*.mp4;*.mov;*.webm;*.avi)|*.mp4;*.mov;*.webm;*.avi|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            Files.Clear();
            foreach (var path in dialog.FileNames)
            {
                // Validate path safety
                if (path.Contains(".."))
                    continue;

                Files.Add(new ImportFileItem
                {
                    FilePath = path,
                    FileName = Path.GetFileName(path),
                    Status = "Ready"
                });
            }

            UpdateFileListVisibility();
            ImportButton.IsEnabled = Files.Count > 0;
        }
    }

    private async void ImportSelected_Click(object sender, RoutedEventArgs e)
    {
        if (Files.Count == 0)
            return;

        ImportButton.IsEnabled = false;
        ProgressPanel.Visibility = Visibility.Visible;
        ImportProgressBar.Maximum = Files.Count;
        ImportProgressBar.Value = 0;

        for (int i = 0; i < Files.Count; i++)
        {
            var file = Files[i];
            try
            {
                file.Status = "Importing...";
                FilesListBox.Items.Refresh();

                // Simulate import - actual import delegates to IMediaLibraryService
                if (_mediaLibraryService != null)
                {
                    await _mediaLibraryService.ImportAsync(file.FilePath);
                }

                file.Status = "Done";
                ImportedPaths.Add(file.FilePath);
            }
            catch (Exception ex)
            {
                file.Status = $"Error: {ex.Message}";
            }

            ImportProgressBar.Value = i + 1;
            ProgressText.Text = $"Imported {i + 1} of {Files.Count}";
            FilesListBox.Items.Refresh();
        }

        ProgressText.Text = $"Import complete. {ImportedPaths.Count} files imported.";
        ImportButton.Content = "Done";
        ImportButton.IsEnabled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = ImportedPaths.Count > 0;
        Close();
    }

    private void UpdateFileListVisibility()
    {
        bool hasFiles = Files.Count > 0;
        EmptyStateText.Visibility = hasFiles ? Visibility.Collapsed : Visibility.Visible;
        FilesListBox.Visibility = hasFiles ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>
/// Represents a file being imported.
/// </summary>
public class ImportFileItem
{
    public string FilePath { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Status { get; set; } = "Ready";
}
