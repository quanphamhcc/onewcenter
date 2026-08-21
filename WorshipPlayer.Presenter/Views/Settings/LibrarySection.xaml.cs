using System.Windows;
using System.Windows.Controls;

namespace WorshipPlayer.Presenter.Views.Settings;

/// <summary>
/// Library and database settings section - path, storage info, reset.
/// </summary>
public partial class LibrarySection : UserControl
{
    public LibrarySection()
    {
        InitializeComponent();
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        // FolderBrowserDialog is WinForms; use OpenFolderDialog or manual path input
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "Select Folder",
            Title = "Select Media Library Folder"
        };

        if (dialog.ShowDialog() == true)
        {
            var folder = System.IO.Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrEmpty(folder))
                LibraryPathTextBox.Text = folder;
        }
    }

    private void ResetLibraryButton_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to reset the library? This will remove all media references from the database.",
            "Confirm Reset",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            // Reset logic would be called here
            FileCountText.Text = "0 files";
            TotalSizeText.Text = "0 MB";
        }
    }
}
