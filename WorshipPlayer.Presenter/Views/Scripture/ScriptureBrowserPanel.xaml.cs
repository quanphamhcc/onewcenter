using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WorshipPlayer.Core.Models.Bible;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Scripture;

/// <summary>
/// Panel for browsing and looking up Bible scripture by reference.
/// Uses IBibleProvider for verse retrieval.
/// </summary>
public partial class ScriptureBrowserPanel : UserControl
{
    private IBibleProvider? _bibleProvider;

    public IBibleProvider? BibleProvider
    {
        get => _bibleProvider;
        set => _bibleProvider = value;
    }

    public event EventHandler<ScriptureLookupResult>? AddToServiceRequested;

    private ScriptureLookupResult? _currentResult;

    public ScriptureBrowserPanel()
    {
        InitializeComponent();
    }

    private void LookUp_Click(object sender, RoutedEventArgs e)
    {
        PerformLookup();
    }

    private void ReferenceTextBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            PerformLookup();
        }
    }

    private async void PerformLookup()
    {
        var reference = ReferenceTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(reference))
        {
            ShowError("Please enter a scripture reference.");
            return;
        }

        if (_bibleProvider == null)
        {
            ShowError("Bible provider is not available.");
            return;
        }

        var translation = GetComboBoxText(TranslationComboBox);

        try
        {
            var parsedRef = await _bibleProvider.ParseReferenceAsync(reference, translation);
            if (parsedRef == null)
            {
                ShowError($"Could not parse reference '{reference}'.");
                return;
            }

            var verses = await _bibleProvider.GetVersesAsync(parsedRef);
            if (verses != null && verses.Count > 0)
            {
                var displayText = string.Join(" ", verses.Select(v => $"{v.Verse} {v.Text}"));
                ResultsText.Text = displayText;
                ResultsText.Visibility = Visibility.Visible;
                EmptyStateText.Visibility = Visibility.Collapsed;
                ErrorText.Visibility = Visibility.Collapsed;
                AddToServiceButton.IsEnabled = true;

                _currentResult = new ScriptureLookupResult
                {
                    Reference = reference,
                    Translation = translation,
                    Text = displayText
                };
            }
            else
            {
                ShowError($"No verses found for '{reference}'.");
            }
        }
        catch (Exception ex)
        {
            ShowError($"Error looking up reference: {ex.Message}");
        }
    }

    private void AddToService_Click(object sender, RoutedEventArgs e)
    {
        if (_currentResult != null)
        {
            AddToServiceRequested?.Invoke(this, _currentResult);
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
        ResultsText.Visibility = Visibility.Collapsed;
        EmptyStateText.Visibility = Visibility.Collapsed;
        AddToServiceButton.IsEnabled = false;
        _currentResult = null;
    }

    private static string GetComboBoxText(ComboBox comboBox)
    {
        if (comboBox.SelectedItem is ComboBoxItem item)
            return item.Content?.ToString() ?? string.Empty;
        return string.Empty;
    }
}

/// <summary>
/// Result of a scripture lookup.
/// </summary>
public class ScriptureLookupResult
{
    public string Reference { get; set; } = string.Empty;
    public string Translation { get; set; } = "KJV";
    public string Text { get; set; } = string.Empty;
    public string? OrganizationId { get; set; }
}
