using System.Windows;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services.Interfaces;
using CoreTextAlignment = WorshipPlayer.Core.Models.TextAlignment;
using CoreVerticalAlignment = WorshipPlayer.Core.Models.VerticalAlignment;

namespace WorshipPlayer.Presenter.Views.Templates;

/// <summary>
/// Template editor window with live preview and 3 settings tabs.
/// </summary>
public partial class TemplateEditorWindow : Window
{
    private ITemplateService? _templateService;
    private SlideTemplate _template = new();

    public ITemplateService? TemplateService
    {
        get => _templateService;
        set => _templateService = value;
    }

    public new SlideTemplate Template
    {
        get => _template;
        set
        {
            _template = value;
            LoadTemplateIntoControls();
        }
    }

    public bool WasSaved { get; private set; }

    public TemplateEditorWindow()
    {
        InitializeComponent();
        Loaded += TemplateEditorWindow_Loaded;
    }

    private void TemplateEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        FormattingTabControl.SettingsChanged += OnSettingsChanged;
        AdvancedTabControl.SettingsChanged += OnSettingsChanged;
        MarginsTabControl.SettingsChanged += OnSettingsChanged;
        LoadTemplateIntoControls();
    }

    private void LoadTemplateIntoControls()
    {
        TitleText.Text = $"Edit Template - {_template.Name}";
        UpdatePreview();
    }

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        // Apply formatting from the Formatting tab
        if (FormattingTabControl != null)
        {
            PreviewText.FontSize = FormattingTabControl.SelectedFontSize;
            PreviewText.FontFamily = new System.Windows.Media.FontFamily(FormattingTabControl.SelectedFontFamily);

            try
            {
                var brush = new System.Windows.Media.BrushConverter().ConvertFromString(FormattingTabControl.SelectedFontColor)
                    as System.Windows.Media.Brush;
                if (brush != null)
                    PreviewText.Foreground = brush;
            }
            catch { }

            PreviewText.FontWeight = FormattingTabControl.IsBold ? FontWeights.Bold : FontWeights.Normal;
            PreviewText.FontStyle = FormattingTabControl.IsItalic ? FontStyles.Italic : FontStyles.Normal;
            PreviewText.TextAlignment = FormattingTabControl.SelectedHAlign;
            PreviewText.VerticalAlignment = FormattingTabControl.SelectedVAlign;
        }

        // Apply margins
        if (MarginsTabControl != null)
        {
            PreviewMarginBorder.Padding = new Thickness(
                MarginsTabControl.LeftMargin,
                MarginsTabControl.TopMargin,
                MarginsTabControl.RightMargin,
                MarginsTabControl.BottomMargin);
        }
    }

    private void SaveButton_Click(object sender, RoutedEventArgs e)
    {
        // Gather all settings into the template
        if (FormattingTabControl != null)
        {
            _template.FontFamily = FormattingTabControl.SelectedFontFamily;
            _template.FontSize = FormattingTabControl.SelectedFontSize;
            _template.FontWeight = FormattingTabControl.IsBold ? "Bold" : "Normal";
            _template.FontItalic = FormattingTabControl.IsItalic;
            _template.FontColor = FormattingTabControl.SelectedFontColor;
            _template.HorizontalAlignment = FormattingTabControl.SelectedHAlign switch
            {
                System.Windows.TextAlignment.Left => CoreTextAlignment.Left,
                System.Windows.TextAlignment.Right => CoreTextAlignment.Right,
                _ => CoreTextAlignment.Center
            };
            _template.VerticalAlignment = FormattingTabControl.SelectedVAlign switch
            {
                System.Windows.VerticalAlignment.Top => CoreVerticalAlignment.Top,
                System.Windows.VerticalAlignment.Bottom => CoreVerticalAlignment.Bottom,
                _ => CoreVerticalAlignment.Center
            };
            _template.HasShadow = FormattingTabControl.HasShadow;
            _template.HasOutline = FormattingTabControl.HasOutline;
        }

        if (AdvancedTabControl != null)
        {
            _template.TextTransform = AdvancedTabControl.SelectedTextTransform;
            _template.LetterSpacing = AdvancedTabControl.SelectedLetterSpacing;
            _template.LineHeight = AdvancedTabControl.SelectedLineHeight;
            _template.LineBackgroundEnabled = AdvancedTabControl.LineBackgroundEnabled;
            _template.LineBackgroundColor = AdvancedTabControl.LineBackgroundColor;
        }

        if (MarginsTabControl != null)
        {
            _template.MarginTop = MarginsTabControl.TopMargin;
            _template.MarginLeft = MarginsTabControl.LeftMargin;
            _template.MarginRight = MarginsTabControl.RightMargin;
            _template.MarginBottom = MarginsTabControl.BottomMargin;
            _template.BorderStyle = MarginsTabControl.SelectedBorderStyle;
        }

        WasSaved = true;
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        WasSaved = false;
        DialogResult = false;
        Close();
    }
}
