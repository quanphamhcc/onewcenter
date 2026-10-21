using System.Windows;
using System.Windows.Controls;

namespace WorshipPlayer.Presenter.Views.Settings;

/// <summary>
/// Defaults settings section - fade time, aspect ratio, default template, automation.
/// </summary>
public partial class DefaultsSection : UserControl
{
    public DefaultsSection()
    {
        InitializeComponent();
    }

    private void FadeTime_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FadeTimeDisplay != null)
            FadeTimeDisplay.Text = $"{(int)FadeTimeSlider.Value}ms";
    }

    private void SlideLead_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SlideLeadDisplay != null)
            SlideLeadDisplay.Text = $"{(int)SlideLeadSlider.Value}ms";
    }

    /// <summary>
    /// Lines per slide (2–4) selected in the combo. Get reads the selected Tag; set selects the
    /// matching item (clamped 2–4). Bound to PresenterSettings.LinesPerSlide by SettingsWindow.
    /// </summary>
    public int LinesPerSlide
    {
        get
        {
            if (LinesPerSlideComboBox?.SelectedItem is ComboBoxItem item &&
                item.Tag is string tag && int.TryParse(tag, out int n))
                return System.Math.Clamp(n, 2, 4);
            return 4;
        }
        set
        {
            int v = System.Math.Clamp(value, 2, 4);
            if (LinesPerSlideComboBox == null) return;
            foreach (var obj in LinesPerSlideComboBox.Items)
            {
                if (obj is ComboBoxItem item && item.Tag is string tag &&
                    int.TryParse(tag, out int n) && n == v)
                {
                    LinesPerSlideComboBox.SelectedItem = item;
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Slide read-ahead lead (ms). Bound to PresenterSettings.SlideLeadMs by SettingsWindow.
    /// </summary>
    public int SlideLeadMs
    {
        get => SlideLeadSlider != null ? (int)SlideLeadSlider.Value : 500;
        set
        {
            if (SlideLeadSlider != null)
                SlideLeadSlider.Value = System.Math.Clamp(value, 0, 1500);
        }
    }
}
