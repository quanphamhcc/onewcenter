using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using WorshipPlayer.Core.Services.Bible;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Settings;

/// <summary>
/// Bibles settings section - manage installed translations, import XML, set default.
/// The translation list is loaded from the Bible database via
/// <see cref="BibleProvider"/> (dot2-T12); the two bundled translations are the
/// fallback shown when no provider is wired. The Import XML button runs the
/// real Zefania import (T11) and reports the concrete result.
/// </summary>
public partial class BiblesSection : UserControl
{
    public ObservableCollection<BibleTranslationItem> Translations { get; } = new();

    /// <summary>
    /// The shared Bible provider (same instance the Scripture panel uses).
    /// Set by SettingsWindow before the section is shown; when null the section
    /// keeps its bundled fallback list and import is unavailable.
    /// </summary>
    public IBibleProvider? BibleProvider { get; set; }

    public BiblesSection()
    {
        InitializeComponent();

        // Fallback list (bundled translations) until the database list loads.
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

        Loaded += async (_, _) => await RefreshTranslationsAsync();
    }

    /// <summary>
    /// Reload the installed-translation list from the Bible database so a
    /// freshly imported translation appears immediately, without restarting
    /// the app. No-op when no provider is wired or the database returns no
    /// translations (the current list is kept in both cases).
    /// </summary>
    public async Task RefreshTranslationsAsync(string? selectAbbreviation = null)
    {
        if (BibleProvider == null) return;

        List<WorshipPlayer.Core.Models.Bible.BibleTranslation> translations;
        try
        {
            translations = await BibleProvider.GetTranslationsAsync();
        }
        catch
        {
            return; // Keep the current list if the database cannot be read.
        }

        if (translations.Count == 0) return;

        Translations.Clear();
        foreach (var t in translations)
        {
            Translations.Add(new BibleTranslationItem
            {
                Name = t.Name,
                Abbreviation = t.Abbreviation,
                // KJV/WEB are the seeded translations that ship with the app.
                IsBundled = t.Id is "KJV" or "WEB",
                IsDefault = t.IsDefault,
                DefaultVisibility = t.IsDefault ? Visibility.Visible : Visibility.Collapsed
            });
        }

        TranslationListBox.Items.Refresh();
        UpdateStorageInfo();

        if (selectAbbreviation != null)
        {
            var imported = Translations.FirstOrDefault(
                t => t.Abbreviation == selectAbbreviation);
            if (imported != null)
                TranslationListBox.SelectedItem = imported;
        }
    }

    private async void ImportXmlButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Bible XML Files (*.xml)|*.xml",
            Title = "Import Bible Translation"
        };

        if (dialog.ShowDialog() != true) return;

        if (BibleProvider == null)
        {
            MessageBox.Show(
                "The Bible database is not available, so the translation cannot be imported.",
                "Import Bible Translation", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ImportXmlButton.IsEnabled = false;
        try
        {
            // Real Zefania import (T11): parses the whole file first and writes
            // translation + books + verses in one transaction; a bad file throws
            // InvalidDataException with the parser's specific reason and leaves
            // the database untouched.
            var translation = await BibleProvider.ImportTranslationAsync(dialog.FileName);
            var books = await BibleProvider.GetBooksAsync(translation.Id);
            var verseCount = await BibleProvider.GetVerseCountAsync(translation.Id);

            await RefreshTranslationsAsync(translation.Abbreviation);

            MessageBox.Show(
                BibleImportSummary.Format(translation, books.Count, verseCount),
                "Import Bible Translation", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (InvalidDataException ex)
        {
            MessageBox.Show(
                $"The selected file could not be imported as a Bible translation.\n\n{ex.Message}",
                "Import Bible Translation", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Import failed: {ex.Message}",
                "Import Bible Translation", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            ImportXmlButton.IsEnabled = true;
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
