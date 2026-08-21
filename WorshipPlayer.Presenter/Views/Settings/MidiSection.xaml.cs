using System.Windows;
using System.Windows.Controls;

namespace WorshipPlayer.Presenter.Views.Settings;

/// <summary>
/// MIDI settings section - device selection and 6-command mapping table.
/// </summary>
public partial class MidiSection : UserControl
{
    public MidiSection()
    {
        InitializeComponent();
    }

    private void TestButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string command)
        {
            // In production, this would fire the command through the presenter engine
            MessageBox.Show($"Test fired: {command}",
                "MIDI Test", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
