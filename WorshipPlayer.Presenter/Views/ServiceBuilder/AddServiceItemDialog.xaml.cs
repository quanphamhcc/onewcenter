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
    /// The library songs offered in the Song picker (property-injected, mirroring the
    /// <see cref="ServiceOrderManager"/> injection style). Each entry's Title + Artist
    /// is shown; picking one links the item via <see cref="ServiceItem.SongFolder"/>.
    /// </summary>
    public IReadOnlyList<Song> SongLibrary { get; set; } = Array.Empty<Song>();

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

    /// <summary>
    /// Backing item for the song picker combo: carries the real <see cref="Song"/> and
    /// a "Title — Artist" display string.
    /// </summary>
    private sealed class SongPickerItem
    {
        public Song Song { get; }
        public string Display { get; }

        public SongPickerItem(Song song)
        {
            Song = song;
            Display = string.IsNullOrWhiteSpace(song.Artist)
                ? song.Title
                : $"{song.Title} — {song.Artist}";
        }
    }

    private void AddServiceItemDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // Populate the song picker from the injected library (used in both add + edit).
        SongPickerComboBox.ItemsSource = SongLibrary
            .Select(s => new SongPickerItem(s))
            .ToList();

        if (ItemToEdit != null)
        {
            // Edit mode: reflect the item's current type and relabel the UI.
            Title = "Edit Service Item";
            AddItemButton.Content = "Save";
            SelectComboBoxItem(ItemTypeComboBox, ItemTypeToText(ItemToEdit.Type));
            ItemTitleTextBox.Text = ItemToEdit.Title;

            // Pre-select the linked song / pre-fill the scripture reference so the
            // picker opens on the item's existing content.
            if (ItemToEdit.Type == ServiceItemType.Song)
            {
                var linked = SongPathResolver.Resolve(SongLibrary, ItemToEdit.SongFolder, ItemToEdit.Title);
                if (linked != null)
                {
                    var match = ((IEnumerable<SongPickerItem>)SongPickerComboBox.ItemsSource)
                        .FirstOrDefault(i => ReferenceEquals(i.Song, linked));
                    if (match != null)
                        SongPickerComboBox.SelectedItem = match;
                }
            }
            else if (ItemToEdit.Type == ServiceItemType.Scripture)
            {
                ScriptureReferenceTextBox.Text = ItemToEdit.ScriptureReference;
            }

            UpdatePickerVisibility();

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

        // Add mode: show the picker that matches the default/selected type.
        UpdatePickerVisibility();
    }

    /// <summary>
    /// Show the Song picker only for Song, the Scripture reference box only for
    /// Scripture, and hide both for every other item type.
    /// </summary>
    private void UpdatePickerVisibility()
    {
        if (SongPickerPanel == null || ScripturePanel == null) return;

        var type = TextToItemType(GetComboBoxText(ItemTypeComboBox));
        SongPickerPanel.Visibility = type == ServiceItemType.Song
            ? Visibility.Visible : Visibility.Collapsed;
        ScripturePanel.Visibility = type == ServiceItemType.Scripture
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ItemTypeComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdatePickerVisibility();
    }

    private void SongPickerComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Auto-fill the Item name from the picked song. The name stays an editable
        // override — the user can still change it afterward.
        if (SongPickerComboBox.SelectedItem is SongPickerItem picked)
            ItemTitleTextBox.Text = picked.Song.Title;
    }

    /// <summary>The library song currently chosen in the picker, or null.</summary>
    private Song? SelectedSong =>
        (SongPickerComboBox.SelectedItem as SongPickerItem)?.Song;

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

                var selectedSong = SelectedSong;
                var scriptureRef = ScriptureReferenceTextBox.Text;

                ItemToEdit.Type = itemType;
                // Single source of truth for the Title across all types.
                ItemToEdit.Title = ServiceItemMapper.BuildTitle(
                    itemType, ItemTitleTextBox.Text, selectedSong, scriptureRef);

                // Re-apply the type-specific link (SongFolder / ScriptureReference).
                if (itemType == ServiceItemType.Song && selectedSong != null)
                    ServiceItemMapper.ApplySong(ItemToEdit, selectedSong);
                else if (itemType == ServiceItemType.Scripture)
                    ServiceItemMapper.ApplyScripture(ItemToEdit, scriptureRef);

                // ApplySong/ApplyScripture may re-set Title; keep BuildTitle as the
                // authoritative value (it already accounts for both).
                ItemToEdit.Title = ServiceItemMapper.BuildTitle(
                    itemType, ItemTitleTextBox.Text, selectedSong, scriptureRef);

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

                var selectedSong = SelectedSong;
                var scriptureRef = ScriptureReferenceTextBox.Text;

                var title = ServiceItemMapper.BuildTitle(
                    itemType, ItemTitleTextBox.Text, selectedSong, scriptureRef);

                var serviceItem = new ServiceItem
                {
                    Type = itemType,
                    Title = title
                };

                // Link the type-specific content (SongFolder / ScriptureReference).
                if (itemType == ServiceItemType.Song && selectedSong != null)
                    ServiceItemMapper.ApplySong(serviceItem, selectedSong);
                else if (itemType == ServiceItemType.Scripture)
                    ServiceItemMapper.ApplyScripture(serviceItem, scriptureRef);

                // Keep the computed Title authoritative (ApplySong/ApplyScripture may
                // have changed it, and both agree with BuildTitle here).
                serviceItem.Title = title;

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
