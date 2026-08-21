using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Presenter.Views.Templates;

/// <summary>
/// Advanced formatting tab - text transform, letter spacing, line height, line background.
/// </summary>
public partial class AdvancedTab : UserControl
{
    public event EventHandler? SettingsChanged;

    public TextTransformType SelectedTextTransform
    {
        get
        {
            var selected = (TextTransformComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return selected switch
            {
                "Uppercase" => TextTransformType.Uppercase,
                "Lowercase" => TextTransformType.Lowercase,
                _ => TextTransformType.None
            };
        }
    }

    public double SelectedLetterSpacing => LetterSpacingSlider.Value;

    public double SelectedLineHeight => LineHeightSlider.Value;

    public bool LineBackgroundEnabled => LineBackgroundToggle.IsChecked == true;

    public string LineBackgroundColor => LineBackgroundColorTextBox.Text;

    public AdvancedTab()
    {
        InitializeComponent();
    }

    private void Setting_Changed(object sender, SelectionChangedEventArgs e)
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LetterSpacing_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LetterSpacingDisplay != null)
            LetterSpacingDisplay.Text = Math.Round(LetterSpacingSlider.Value, 1).ToString("F1");
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LineHeight_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LineHeightDisplay != null)
            LineHeightDisplay.Text = Math.Round(LineHeightSlider.Value, 1).ToString("F1");
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Setting_Checked(object sender, RoutedEventArgs e)
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ColorHex_Changed(object sender, TextChangedEventArgs e)
    {
        if (sender == LineBackgroundColorTextBox && LineBackgroundSwatch != null)
        {
            try
            {
                var brush = new BrushConverter().ConvertFromString(LineBackgroundColorTextBox.Text) as Brush;
                if (brush != null)
                    LineBackgroundSwatch.Background = brush;
            }
            catch { }
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
