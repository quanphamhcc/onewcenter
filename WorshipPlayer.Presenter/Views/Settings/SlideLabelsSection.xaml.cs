using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Views.Settings;

/// <summary>
/// Slide Labels settings section (đợt 2 T8 / R5.2) — edit the shared label catalog:
/// rename, add, delete, recolor (hex), and assign hotkeys. Works on a deep COPY of the
/// catalog; <see cref="SettingsWindow"/> reads it back via <see cref="GetLabels"/> only on
/// Save, and refuses to save while <see cref="DuplicateHotkey"/> is non-null (the T7
/// <see cref="SlideLabelService.FindDuplicateHotkey"/> rule — duplicates must block a save).
/// </summary>
public partial class SlideLabelsSection : UserControl
{
    private readonly ObservableCollection<SlideLabelDefinition> _labels = new();

    public SlideLabelsSection()
    {
        InitializeComponent();
        LabelsGrid.ItemsSource = _labels;
        LabelsGrid.CellEditEnding += (_, _) => RefreshDuplicateWarning();
    }

    /// <summary>Replace the editor contents with deep copies of the given catalog.</summary>
    public void SetLabels(IEnumerable<SlideLabelDefinition> labels)
    {
        _labels.Clear();
        foreach (var label in labels)
            _labels.Add(Copy(label));
        RefreshDuplicateWarning();
    }

    /// <summary>Deep copies of the catalog as currently edited. Read on Save.</summary>
    public List<SlideLabelDefinition> GetLabels() => _labels.Select(Copy).ToList();

    /// <summary>
    /// The duplicated hotkey in the current edit state (the stored text of the first label
    /// that has a duplicate partner), or null when the catalog may be saved.
    /// </summary>
    public string? DuplicateHotkey => SlideLabelService.FindDuplicateHotkey(_labels);

    private void AddButton_Click(object sender, RoutedEventArgs e)
    {
        var label = new SlideLabelDefinition
        {
            Id = "label-" + Guid.NewGuid().ToString("N")[..8],
            Name = "New Label",
            Color = "#4A90D9",
            Hotkey = ""
        };
        _labels.Add(label);
        LabelsGrid.SelectedItem = label;
        RefreshDuplicateWarning();
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (LabelsGrid.SelectedItem is SlideLabelDefinition selected)
        {
            _labels.Remove(selected);
            RefreshDuplicateWarning();
        }
    }

    private void RefreshDuplicateWarning()
    {
        var dup = DuplicateHotkey;
        if (dup != null)
        {
            DuplicateWarningText.Text =
                $"Two labels share the hotkey \"{dup}\". Saving is blocked until each label has its own hotkey (or one is cleared).";
            DuplicateWarningText.Visibility = Visibility.Visible;
        }
        else
        {
            DuplicateWarningText.Text = "";
            DuplicateWarningText.Visibility = Visibility.Collapsed;
        }
    }

    private static SlideLabelDefinition Copy(SlideLabelDefinition label) => new()
    {
        Id = label.Id,
        Name = label.Name,
        Color = label.Color,
        Hotkey = label.Hotkey
    };
}

/// <summary>Hex string ("#RRGGBB") → brush for the label color preview; gray when unparsable.</summary>
public class HexBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex.Trim()));
            }
            catch (FormatException) { /* fall through to the placeholder brush */ }
        }
        return new SolidColorBrush(Colors.Gray);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
