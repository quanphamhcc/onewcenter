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
    /// When set (before ShowDialog), the dialog opens in EDIT mode: fields are
    /// pre-populated from this item, and on confirm the item is mutated in place
    /// instead of a new one being added.
    /// </summary>
    public ServiceItem? ItemToEdit { get; set; }

    /// <summary>
    /// Fired when an item has been successfully added to the service order.
    /// </summary>
    public event EventHandler<ServiceItem>? ItemAdded;

    /// <summary>
    /// Fired when an existing item has been edited (edit mode only).
    /// </summary>
    public event EventHandler<ServiceItem>? ItemEdited;

    public AddServiceItemDialog()
    {
        InitializeComponent();
        Loaded += AddServiceItemDialog_Loaded;
    }

    private void AddServiceItemDialog_Loaded(object sender, RoutedEventArgs e)
    {
        if (ItemToEdit == null) return;

        // Edit mode: reflect the item's current type and relabel the UI.
        Title = "Edit Service Item";
        AddItemButton.Content = "Save";
        SelectComboBoxItem(ItemTypeComboBox, ItemTypeToText(ItemToEdit.Type));
    }

    private async void AddItem_Click(object sender, RoutedEventArgs e)
    {
        SelectedItemType = GetComboBoxText(ItemTypeComboBox);
        SelectedBackgroundType = GetComboBoxText(BackgroundTypeComboBox);
        SelectedForegroundType = GetComboBoxText(ForegroundTypeComboBox);
        SelectedGoTo = GetComboBoxText(GoToComboBox);
        SelectedAfterCondition = GetComboBoxText(AfterConditionComboBox);
        TransitionTimeMs = (int)TransitionSlider.Value;
        AudioFilePath = string.IsNullOrWhiteSpace(AudioPathTextBox.Text) ? null : AudioPathTextBox.Text;

        Confirmed = true;

        var itemType = TextToItemType(SelectedItemType);

        try
        {
            if (ItemToEdit != null)
            {
                // EDIT: mutate the existing item in place. The operator window owns
                // the save after ItemEdited (one save path), so no SaveAsync here.
                ItemToEdit.Type = itemType;
                ItemEdited?.Invoke(this, ItemToEdit);
            }
            else if (ServiceOrderManager != null)
            {
                // Ensure a service order exists
                if (ServiceOrderManager.CurrentServiceOrder == null)
                {
                    ServiceOrderManager.CreateNew("New Service", DateTime.Today.ToString("yyyy-MM-dd"));
                }

                var serviceItem = new ServiceItem
                {
                    Type = itemType,
                    Title = $"{SelectedItemType} Item"
                };

                ServiceOrderManager.AddItem(serviceItem);

                // Persist immediately so the added item survives restart (core bug fix).
                await ServiceOrderManager.SaveAsync();

                ItemAdded?.Invoke(this, serviceItem);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to save service item: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }

        DialogResult = true;
        Close();
    }

    private static ServiceItemType TextToItemType(string text) => text switch
    {
        "Song" => ServiceItemType.Song,
        "Scripture" => ServiceItemType.Scripture,
        "Announcement" => ServiceItemType.Announcement,
        "Sermon" => ServiceItemType.Sermon,
        "Video" => ServiceItemType.Video,
        "Offering" => ServiceItemType.Offering,
        "Prayer" => ServiceItemType.Prayer,
        "Blank" => ServiceItemType.Blank,
        "Custom" => ServiceItemType.Custom,
        _ => ServiceItemType.Song
    };

    private static string ItemTypeToText(ServiceItemType type) => type switch
    {
        ServiceItemType.Song => "Song",
        ServiceItemType.Scripture => "Scripture",
        ServiceItemType.Announcement => "Announcement",
        ServiceItemType.Sermon => "Sermon",
        ServiceItemType.Video => "Video",
        ServiceItemType.Offering => "Offering",
        ServiceItemType.Prayer => "Prayer",
        ServiceItemType.Blank => "Blank",
        ServiceItemType.Custom => "Custom",
        _ => "Song"
    };

    private static void SelectComboBoxItem(System.Windows.Controls.ComboBox comboBox, string text)
    {
        foreach (var obj in comboBox.Items)
        {
            if (obj is System.Windows.Controls.ComboBoxItem item &&
                string.Equals(item.Content?.ToString(), text, StringComparison.Ordinal))
            {
                comboBox.SelectedItem = item;
                return;
            }
        }
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
