using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace WorshipPlayer.Presenter.Views.Settings;

/// <summary>
/// Bibles settings section - manage installed translations, import XML, set default.
/// </summary>
public partial class BiblesSection : UserControl
{
    public ObservableCollection<BibleTranslationItem> Translations { get; } = new();

    public BiblesSection()
    {
        InitializeComponent();

        // Add bundled translations
        Translations.Add(new BibleTranslationItem
        {
            Name = "King James Version",
            Abbreviation = "KJV",
            IsBundled = true,
            IsDefault = true,
            DefaultVisibility = Visibility.Visible
        });
        Translations.Add(new BibleTranslationItem
        {
            Name = "World English Bible",
            Abbreviation = "WEB",
            IsBundled = true,
            IsDefault = false,
            DefaultVisibility = Visibility.Collapsed
        });

        TranslationListBox.ItemsSource = Translations;
        UpdateStorageInfo();
    }

    private void ImportXmlButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Bible XML Files (*.xml)|*.xml",
            Title = "Import Bible Translation"
        };

        if (dialog.ShowDialog() == true)
        {
            // Would parse and import the Bible XML
            MessageBox.Show($"Bible import from: {dialog.FileName}",
                "Import", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void SetDefaultButton_Click(object sender, RoutedEventArgs e)
    {
        if (TranslationListBox.SelectedItem is not BibleTranslationItem selected) return;

        foreach (var t in Translations)
        {
            t.IsDefault = false;
            t.DefaultVisibility = Visibility.Collapsed;
        }

        selected.IsDefault = true;
        selected.DefaultVisibility = Visibility.Visible;
        TranslationListBox.Items.Refresh();
    }

    private void RemoveButton_Click(object sender, RoutedEventArgs e)
    {
        if (TranslationListBox.SelectedItem is not BibleTranslationItem selected) return;
        if (selected.IsBundled) return;

        Translations.Remove(selected);
        UpdateStorageInfo();
    }

    private void TranslationList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TranslationListBox.SelectedItem is BibleTranslationItem item)
        {
            RemoveButton.IsEnabled = !item.IsBundled;
        }
    }

    private void UpdateStorageInfo()
    {
        StorageInfoText.Text = $"{Translations.Count} translations installed";
    }
}

/// <summary>
/// Display model for a Bible translation in the list.
/// </summary>
public class BibleTranslationItem
{
    public string Name { get; set; } = string.Empty;
    public string Abbreviation { get; set; } = string.Empty;
    public bool IsBundled { get; set; }
    public bool IsDefault { get; set; }
    public Visibility DefaultVisibility { get; set; } = Visibility.Collapsed;
}
