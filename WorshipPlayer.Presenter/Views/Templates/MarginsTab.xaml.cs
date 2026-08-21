using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Presenter.Views.Templates;

/// <summary>
/// Margins tab - top/left/right/bottom margins and border style.
/// </summary>
public partial class MarginsTab : UserControl
{
    public event EventHandler? SettingsChanged;

    public double TopMargin => TopMarginSlider.Value;
    public double LeftMargin => LeftMarginSlider.Value;
    public double RightMargin => RightMarginSlider.Value;
    public double BottomMargin => BottomMarginSlider.Value;

    public BorderStyleType SelectedBorderStyle
    {
        get
        {
            var selected = (BorderStyleComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString();
            return selected switch
            {
                "Solid" => BorderStyleType.Solid,
                "Dashed" => BorderStyleType.Dashed,
                _ => BorderStyleType.None
            };
        }
    }

    public MarginsTab()
    {
        InitializeComponent();
    }

    private void TopMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TopMarginDisplay != null)
            TopMarginDisplay.Text = ((int)TopMarginSlider.Value).ToString();
        UpdateMarginIndicator();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void LeftMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (LeftMarginDisplay != null)
            LeftMarginDisplay.Text = ((int)LeftMarginSlider.Value).ToString();
        UpdateMarginIndicator();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void RightMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (RightMarginDisplay != null)
            RightMarginDisplay.Text = ((int)RightMarginSlider.Value).ToString();
        UpdateMarginIndicator();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BottomMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (BottomMarginDisplay != null)
            BottomMarginDisplay.Text = ((int)BottomMarginSlider.Value).ToString();
        UpdateMarginIndicator();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void BorderStyle_Changed(object sender, SelectionChangedEventArgs e)
    {
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void UpdateMarginIndicator()
    {
        if (MarginIndicator == null) return;

        // Scale margins to fit preview (divide by ~5 for visual representation)
        double scale = 0.3;
        MarginIndicator.Margin = new Thickness(
            LeftMargin * scale,
            TopMargin * scale,
            RightMargin * scale,
            BottomMargin * scale);
    }
}
