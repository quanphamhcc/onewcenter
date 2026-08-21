using System.Windows;
using Microsoft.Win32;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Views.ServiceBuilder;

/// <summary>
/// Dialog for adding a new service item with type, background, auto-advance, and transition settings.
/// </summary>
public partial class AddServiceItemDialog : Window
{
    public string SelectedItemType { get; private set; } = "Song";
    public string SelectedBackgroundType { get; private set; } = "None";
    public string SelectedForegroundType { get; private set; } = "Text";
    public string SelectedGoTo { get; private set; } = "Next Item";
    public string SelectedAfterCondition { get; private set; } = "Manual";
    public int TransitionTimeMs { get; private set; } = 500;
    public string? AudioFilePath { get; private set; }
    public bool Confirmed { get; private set; }

    /// <summary>
    /// The service order manager to add items to.
    /// </summary>
    public ServiceOrderManager? ServiceOrderManager { get; set; }

    /// <summary>
    /// Fired when an item has been successfully added to the service order.
    /// </summary>
    public event EventHandler<ServiceItem>? ItemAdded;

    public AddServiceItemDialog()
    {
        InitializeComponent();
    }

    private void AddItem_Click(object sender, RoutedEventArgs e)
    {
        SelectedItemType = GetComboBoxText(ItemTypeComboBox);
        SelectedBackgroundType = GetComboBoxText(BackgroundTypeComboBox);
        SelectedForegroundType = GetComboBoxText(ForegroundTypeComboBox);
        SelectedGoTo = GetComboBoxText(GoToComboBox);
        SelectedAfterCondition = GetComboBoxText(AfterConditionComboBox);
        TransitionTimeMs = (int)TransitionSlider.Value;
        AudioFilePath = string.IsNullOrWhiteSpace(AudioPathTextBox.Text) ? null : AudioPathTextBox.Text;

        Confirmed = true;

        // Create a ServiceItem and add it to the ServiceOrderManager
        if (ServiceOrderManager != null)
        {
            // Ensure a service order exists
            if (ServiceOrderManager.CurrentServiceOrder == null)
            {
                ServiceOrderManager.CreateNew("New Service", DateTime.Today.ToString("yyyy-MM-dd"));
            }

            var itemType = SelectedItemType switch
            {
                "Song" => ServiceItemType.Song,
                "Scripture" => ServiceItemType.Scripture,
                "Announcement" => ServiceItemType.Announcement,
                "Sermon" => ServiceItemType.Sermon,
                "Video" => ServiceItemType.Video,
                "Offering" => ServiceItemType.Offering,
                "Prayer" => ServiceItemType.Prayer,
                _ => ServiceItemType.Song
            };

            var serviceItem = new ServiceItem
            {
                Type = itemType,
                Title = $"{SelectedItemType} Item"
            };

            ServiceOrderManager.AddItem(serviceItem);
            ItemAdded?.Invoke(this, serviceItem);
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    private void BrowseAudio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Audio File",
            Filter = "Audio Files (*.mp3;*.wav;*.flac;*.ogg;*.m4a)|*.mp3;*.wav;*.flac;*.ogg;*.m4a|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            AudioPathTextBox.Text = dialog.FileName;
        }
    }

    private void TransitionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (TransitionValueText != null)
        {
            TransitionValueText.Text = ((int)e.NewValue).ToString();
        }
    }

    private static string GetComboBoxText(System.Windows.Controls.ComboBox comboBox)
    {
        if (comboBox.SelectedItem is System.Windows.Controls.ComboBoxItem item)
        {
            return item.Content?.ToString() ?? string.Empty;
        }
        return string.Empty;
    }
}
