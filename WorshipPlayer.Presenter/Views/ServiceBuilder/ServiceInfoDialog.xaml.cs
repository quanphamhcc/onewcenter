using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Views.ServiceBuilder;

/// <summary>
/// Dialog to set a service's Name and a US-format Service date (MM/DD/YYYY with an
/// optional 12-hour hh:mm AM/PM time). Used at create time (and optionally to edit an
/// existing service). This is its own Window that MERGES OwcDesignSystem.xaml, so it
/// may safely use the keyed dark styles — unlike OperatorWindow, which hardcodes brushes.
///
/// US formatting is enforced via the en-US culture regardless of OS locale.
/// </summary>
public partial class ServiceInfoDialog : Window
{
    /// <summary>Chosen service name.</summary>
    public string ServiceName { get; private set; } = "New Service";

    /// <summary>Chosen date normalized to "yyyy-MM-dd" for storage.</summary>
    public string ServiceDate { get; private set; } = DateTime.Today.ToString(ServiceDateFormatter.StorageDateFormat);

    /// <summary>Optional start date+time (null = date-only service).</summary>
    public DateTime? StartTime { get; private set; }

    /// <summary>True when the user confirmed (clicked Create/Save).</summary>
    public bool Confirmed { get; private set; }

    public ServiceInfoDialog()
    {
        InitializeComponent();

        // Force US date rendering (MM/DD/YYYY) and the en-US calendar regardless of OS locale.
        DatePickerControl.SelectedDateFormat = DatePickerFormat.Short;
        DatePickerControl.Language = XmlLanguage.GetLanguage("en-US");

        Loaded += ServiceInfoDialog_Loaded;
    }

    private void ServiceInfoDialog_Loaded(object sender, RoutedEventArgs e)
    {
        // Default when nothing was pre-filled: today's date, empty name/time.
        if (DatePickerControl.SelectedDate == null)
            DatePickerControl.SelectedDate = DateTime.Today;

        NameTextBox.Focus();
        NameTextBox.SelectAll();
    }

    /// <summary>
    /// Pre-fill the dialog for editing an existing service. Call before ShowDialog().
    /// </summary>
    public void Initialize(string name, string date, DateTime? startTime)
    {
        Title = "Edit Service";
        SaveButton.Content = "Save";

        NameTextBox.Text = name;

        if (ServiceDateFormatter.TryParseStoredDate(date, out var parsedDate))
            DatePickerControl.SelectedDate = parsedDate.Date;

        if (startTime.HasValue)
            TimeTextBox.Text = ServiceDateFormatter.FormatTime(startTime.Value.TimeOfDay);
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowValidation("Please enter a service name.");
            return;
        }

        if (DatePickerControl.SelectedDate == null)
        {
            ShowValidation("Please pick a service date.");
            return;
        }

        var date = DatePickerControl.SelectedDate.Value.Date;

        DateTime? startTime = null;
        var timeText = TimeTextBox.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(timeText))
        {
            if (!ServiceDateFormatter.TryParseTime(timeText, out var time))
            {
                ShowValidation("Time must be like 08:00 AM (hh:mm AM/PM). Leave blank for no time.");
                return;
            }
            startTime = date.Add(time);
        }

        ServiceName = name!;
        ServiceDate = date.ToString(ServiceDateFormatter.StorageDateFormat);
        StartTime = startTime;
        Confirmed = true;

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = false;
        DialogResult = false;
        Close();
    }

    private void ShowValidation(string message)
    {
        ValidationText.Text = message;
        ValidationText.Visibility = Visibility.Visible;
    }
}
