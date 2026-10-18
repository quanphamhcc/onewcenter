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
    /// When true (set before ShowDialog), the dialog shows the Service name + date
    /// fields and creates a BRAND-NEW service order on confirm, regardless of whether
    /// a service is already loaded. When false, the dialog just adds an item to the
    /// current service (and only falls back to creating one if none exists).
    /// </summary>
    public bool CreateNewService { get; set; }

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
        if (ItemToEdit != null)
        {
            // Edit mode: reflect the item's current type and relabel the UI.
            Title = "Edit Service Item";
            AddItemButton.Content = "Save";
            SelectComboBoxItem(ItemTypeComboBox, ItemTypeToText(ItemToEdit.Type));
            ItemTitleTextBox.Text = ItemToEdit.Title;

            // Also surface the current service's name + date so they can be edited
            // from the same dialog (phuong an A).
            var order = ServiceOrderManager?.CurrentServiceOrder;
            if (order != null)
            {
                NewServicePanel.Visibility = Visibility.Visible;
                ServiceNameTextBox.Text = order.Name;
                ServiceDateTextBox.Text = ServiceDateFormatter.FormatUs(order.Date, order.StartTime);
            }
            return;
        }

        // Show the Service name + date fields when the caller explicitly wants to
        // create a new service, OR when there is no service loaded yet (first run).
        // The app usually auto-loads a service on startup, so relying on
        // CurrentServiceOrder == null alone would hide the fields for the common
        // "+ Add a new service" path — hence the explicit CreateNewService flag.
        var creatingNewService = CreateNewService
            || (ServiceOrderManager != null && ServiceOrderManager.CurrentServiceOrder == null);

        if (creatingNewService)
        {
            NewServicePanel.Visibility = Visibility.Visible;
            ServiceNameTextBox.Text = "New Service";
            // Prefill today's date in US format (time optional, left blank).
            ServiceDateTextBox.Text = DateTime.Today.ToString(
                ServiceDateFormatter.UsDateFormat,
                System.Globalization.CultureInfo.GetCultureInfo("en-US"));
        }
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
                // EDIT: also apply any changes to the current service's name + date
                // from the New-service fields (phuong an A). The operator window owns
                // the save after ItemEdited (one save path), so no SaveAsync here.
                var order = ServiceOrderManager?.CurrentServiceOrder;
                if (order != null)
                {
                    order.Name = string.IsNullOrWhiteSpace(ServiceNameTextBox.Text)
                        ? order.Name
                        : ServiceNameTextBox.Text.Trim();

                    var dateInput = ServiceDateTextBox.Text;
                    if (!string.IsNullOrWhiteSpace(dateInput))
                    {
                        if (ServiceDateFormatter.TryParseUs(dateInput, out var storedDate, out var startTime))
                        {
                            order.Date = storedDate;
                            order.StartTime = startTime;
                        }
                        else
                        {
                            Confirmed = false;
                            MessageBox.Show(
                                "Please enter the service date as MM/DD/YYYY, with an optional time like 08:00 AM.",
                                "Invalid service date",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                            return;
                        }
                    }
                }

                ItemToEdit.Type = itemType;
                if (!string.IsNullOrWhiteSpace(ItemTitleTextBox.Text))
                    ItemToEdit.Title = ItemTitleTextBox.Text.Trim();
                ItemEdited?.Invoke(this, ItemToEdit);
            }
            else if (ServiceOrderManager != null)
            {
                // Create a brand-new service when the caller asked for one (or when
                // none exists yet), using the name + date the user entered. Otherwise
                // just add the item to the current service.
                if (CreateNewService || ServiceOrderManager.CurrentServiceOrder == null)
                {
                    var name = string.IsNullOrWhiteSpace(ServiceNameTextBox.Text)
                        ? "New Service"
                        : ServiceNameTextBox.Text.Trim();

                    string storedDate;
                    DateTime? startTime;
                    var dateInput = ServiceDateTextBox.Text;
                    if (string.IsNullOrWhiteSpace(dateInput))
                    {
                        storedDate = DateTime.Today.ToString(ServiceDateFormatter.StorageDateFormat);
                        startTime = null;
                    }
                    else if (!ServiceDateFormatter.TryParseUs(dateInput, out storedDate, out startTime))
                    {
                        // Invalid date: don't create a service; let the user fix it.
                        Confirmed = false;
                        MessageBox.Show(
                            "Please enter the service date as MM/DD/YYYY, with an optional time like 08:00 AM.",
                            "Invalid service date",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                        return;
                    }

                    ServiceOrderManager.CreateNew(name, storedDate);
                    if (ServiceOrderManager.CurrentServiceOrder != null)
                        ServiceOrderManager.CurrentServiceOrder.StartTime = startTime;
                }

                var title = string.IsNullOrWhiteSpace(ItemTitleTextBox.Text)
                    ? $"{SelectedItemType} Item"
                    : ItemTitleTextBox.Text.Trim();

                var serviceItem = new ServiceItem
                {
                    Type = itemType,
                    Title = title
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
