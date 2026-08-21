using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Tools;

/// <summary>
/// Alert tool dialog for displaying overlay text on outputs.
/// </summary>
public partial class AlertToolDialog : Window
{
    private IAlertService? _alertService;

    public IAlertService? AlertService
    {
        get => _alertService;
        set
        {
            _alertService = value;
            _ = LoadTemplatesAsync();
        }
    }

    public event EventHandler<AlertTemplate>? AlertFired;

    public AlertToolDialog()
    {
        InitializeComponent();
    }

    private async Task LoadTemplatesAsync()
    {
        if (_alertService == null) return;

        var templates = await _alertService.GetTemplatesAsync();
        TemplateComboBox.Items.Clear();
        TemplateComboBox.Items.Add(new ComboBoxItem { Content = "(No template)", IsSelected = true });
        foreach (var t in templates)
        {
            TemplateComboBox.Items.Add(new ComboBoxItem { Content = t.Name, Tag = t });
        }
    }

    private void FontSizeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FontSizeDisplay != null)
            FontSizeDisplay.Text = ((int)FontSizeSlider.Value).ToString();
    }

    private void TemplateComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateComboBox.SelectedItem is ComboBoxItem item && item.Tag is AlertTemplate template)
        {
            AlertTextBox.Text = template.Text;
            FontSizeSlider.Value = template.FontSize;
            ColorHexTextBox.Text = template.FontColor;
            ShadowToggle.IsChecked = template.HasShadow;
            OutlineToggle.IsChecked = template.HasOutline;
            StageDisplayCheckBox.IsChecked = template.ShowOnStageDisplay;
        }
    }

    private async void ShowAlertButton_Click(object sender, RoutedEventArgs e)
    {
        var template = BuildAlertTemplate();

        if (_alertService != null)
        {
            await _alertService.ShowAlertAsync(template);
        }

        AlertFired?.Invoke(this, template);
    }

    private async void SaveTemplateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_alertService == null) return;

        var template = BuildAlertTemplate();
        template.Name = $"Alert {DateTime.Now:HH:mm:ss}";

        await _alertService.SaveTemplateAsync(template);
        await LoadTemplatesAsync();
    }

    private AlertTemplate BuildAlertTemplate()
    {
        var fontFamily = (FontFamilyComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Segoe UI";

        return new AlertTemplate
        {
            Text = AlertTextBox.Text,
            FontSize = FontSizeSlider.Value,
            FontFamily = fontFamily,
            FontColor = ColorHexTextBox.Text,
            HasShadow = ShadowToggle.IsChecked == true,
            HasOutline = OutlineToggle.IsChecked == true,
            ShowOnStageDisplay = StageDisplayCheckBox.IsChecked == true
        };
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
