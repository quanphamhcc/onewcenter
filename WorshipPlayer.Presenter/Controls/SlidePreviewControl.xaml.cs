using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WorshipPlayer.Presenter.Controls;

/// <summary>
/// A reusable WPF UserControl for rendering miniature slide previews.
/// Shows text centered on a dark background with a small font.
/// When IsActive, shows a blue border (#4A90D9) to indicate the current slide.
/// </summary>
public partial class SlidePreviewControl : UserControl
{
    private static readonly SolidColorBrush ActiveBorderBrush =
        new(Color.FromRgb(0x00, 0xD4, 0xFF)); // #00D4FF

    private static readonly SolidColorBrush InactiveBorderBrush =
        new(Colors.Transparent);

    private string _slideText = "";
    private bool _isActive = false;
    private string _sectionLabel = "";

    /// <summary>
    /// The text content to display in the preview.
    /// </summary>
    public string SlideText
    {
        get => _slideText;
        set
        {
            _slideText = value;
            UpdateDisplay();
        }
    }

    /// <summary>
    /// Whether this slide is the currently active/displayed slide.
    /// When true, shows a highlighted blue border.
    /// </summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            _isActive = value;
            UpdateBorder();
        }
    }

    /// <summary>
    /// Label for the section this slide belongs to (e.g., "VERSE 1", "CHORUS").
    /// Displayed above the preview text.
    /// </summary>
    public string SectionLabel
    {
        get => _sectionLabel;
        set
        {
            _sectionLabel = value;
            UpdateSectionLabel();
        }
    }

    public SlidePreviewControl()
    {
        InitializeComponent();
    }

    private void UpdateDisplay()
    {
        SlideTextBlock.Text = _slideText;
    }

    private void UpdateBorder()
    {
        PreviewBorder.BorderBrush = _isActive ? ActiveBorderBrush : InactiveBorderBrush;
    }

    private void UpdateSectionLabel()
    {
        SectionLabelText.Text = _sectionLabel;
        SectionLabelText.Visibility = string.IsNullOrEmpty(_sectionLabel)
            ? Visibility.Collapsed
            : Visibility.Visible;
    }
}
