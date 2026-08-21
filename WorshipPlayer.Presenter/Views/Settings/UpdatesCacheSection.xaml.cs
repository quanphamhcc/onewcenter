using System.Windows;
using System.Windows.Controls;

namespace WorshipPlayer.Presenter.Views.Settings;

/// <summary>
/// Updates and cache management section - version, auto-update, cache clearing.
/// </summary>
public partial class UpdatesCacheSection : UserControl
{
    public UpdatesCacheSection()
    {
        InitializeComponent();
    }

    private void ClearThumbnailCacheButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Clear the thumbnail cache? Thumbnails will be regenerated as needed.",
            "Clear Cache",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            ThumbnailCacheSizeText.Text = "0 MB";
        }
    }

    private void ClearTempFilesButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Clear temporary files?",
            "Clear Temp",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            TempFilesSizeText.Text = "0 MB";
        }
    }
}
