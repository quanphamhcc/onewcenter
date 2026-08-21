using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WorshipPlayer.Presenter.Views.Templates;

/// <summary>
/// Formatting tab for template editor - font, color, alignment, effects.
/// </summary>
public partial class FormattingTab : UserControl
{
    public event EventHandler? SettingsChanged;

    public string SelectedFontFamily =>
        (FontFamilyComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Segoe UI";

    public double SelectedFontSize => FontSizeSlider.Value;

    public bool IsBold => BoldToggle.IsChecked == true;

    public bool IsItalic => ItalicToggle.IsChecked == true;

    public string SelectedFontColor => ColorHexTextBox.Text;

    public TextAlignment SelectedHAlign
    {
        get
        {
            if (HAlignLeft.IsChecked == true) return TextAlignment.Left;
            if (HAlignRight.IsChecked == true) return TextAlignment.Right;
            return TextAlignment.Center;
        }
    }

    public VerticalAlignment SelectedVAlign
    {
        get
        {
            if (VAlignTop.IsChecked == true) return VerticalAlignment.Top;
            if (VAlignBottom.IsChecked == true) return VerticalAlignment.Bottom;
            return VerticalAlignment.Center;
        }
    }

    public bool HasShadow => ShadowToggle.IsChecked == true;

    public bool HasOutline => OutlineToggle.IsChecked == true;

    public FormattingTab()
    {
        InitializeComponent();
    }

    private void Setting_Changed(object sender, SelectionChangedEventArgs e)
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Slider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FontSizeDisplay != null)
            FontSizeDisplay.Text = ((int)FontSizeSlider.Value).ToString();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Setting_Checked(object sender, RoutedEventArgs e)
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void ColorHex_Changed(object sender, TextChangedEventArgs e)
    {
        // Update color swatch preview
        if (sender == ColorHexTextBox && ColorSwatch != null)
        {
            try
            {
                var brush = new BrushConverter().ConvertFromString(ColorHexTextBox.Text) as Brush;
                if (brush != null)
                    ColorSwatch.Background = brush;
            }
            catch { }
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Alignment_Changed(object sender, RoutedEventArgs e)
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }
}
