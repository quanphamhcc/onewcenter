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
}
