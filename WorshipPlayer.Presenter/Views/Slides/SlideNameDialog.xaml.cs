using System.Windows;
using System.Windows.Controls;

namespace WorshipPlayer.Presenter.Views.Slides;

/// <summary>
/// Small modal dialog to name a new Slides presentation. Its own Window that MERGES
/// OwcDesignSystem.xaml, so it may use the keyed dark styles (unlike OperatorWindow). The text box
/// is pre-filled with "New Presentation" and all text is selected on load so the user can type
/// over it immediately; OK is disabled while the box is blank/whitespace.
/// </summary>
public partial class SlideNameDialog : Window
{
    /// <summary>The trimmed name the user entered (valid only when the dialog returned true).</summary>
    public string PresentationName => NameTextBox.Text.Trim();

    public SlideNameDialog()
    {
        InitializeComponent();
        Loaded += SlideNameDialog_Loaded;
    }

    private void SlideNameDialog_Loaded(object sender, RoutedEventArgs e)
    {
        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    private void NameTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // OkButton is null during InitializeComponent's first TextChanged for the initial Text.
        if (OkButton != null)
            OkButton.IsEnabled = !string.IsNullOrWhiteSpace(NameTextBox.Text);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameTextBox.Text))
            return;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
