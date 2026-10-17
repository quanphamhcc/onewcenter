using System.Windows.Controls;

namespace WorshipPlayer.Presenter.Views.Settings;

/// <summary>
/// Footer settings section - CCLI license number, location, font, visibility.
/// </summary>
public partial class FooterSection : UserControl
{
    public FooterSection()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Load the current CCLI license number into the editor (called when the section is created,
    /// mirroring ScreenConfigSection.SetCurrentConfig).
    /// </summary>
    public void SetCcliLicenseNumber(string? ccliLicenseNumber)
    {
        CcliNumberTextBox.Text = ccliLicenseNumber ?? string.Empty;
    }

    /// <summary>
    /// The CCLI license number currently entered in the editor. Read on Save.
    /// </summary>
    public string CcliLicenseNumber => CcliNumberTextBox.Text ?? string.Empty;
}
