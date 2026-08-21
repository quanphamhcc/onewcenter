using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Tools;

/// <summary>
/// Countdown tool dialog supporting Countdown to Zero, Countdown to Time, and Count Up modes.
/// </summary>
public partial class CountdownToolDialog : Window
{
    private ICountdownService? _countdownService;

    public ICountdownService? CountdownService
    {
        get => _countdownService;
        set
        {
            _countdownService = value;
            WireCountdownEvents();
        }
    }

    public CountdownToolDialog()
    {
        InitializeComponent();
    }

    private void WireCountdownEvents()
    {
        if (_countdownService == null) return;

        _countdownService.Tick += (_, ms) =>
        {
            Dispatcher.Invoke(() =>
            {
                var ts = TimeSpan.FromMilliseconds(ms);
                PreviewTimeText.Text = $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}";
            });
        };

        _countdownService.Completed += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                PreviewTimeText.Text = "00:00:00";
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                PauseButton.IsEnabled = false;
                PauseButton.Content = "Pause";
            });
        };
    }

    private void TypeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Adjust UI based on type
        if (TypeComboBox.SelectedIndex == 2) // Count Up
        {
            PreviewTimeText.Text = "00:00:00";
        }
    }

    private async void StartButton_Click(object sender, RoutedEventArgs e)
    {
        if (_countdownService == null) return;

        var config = BuildConfig();
        await _countdownService.StartAsync(config);

        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        PauseButton.IsEnabled = true;
    }

    private async void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_countdownService == null) return;

        await _countdownService.StopAsync();
        StartButton.IsEnabled = true;
        StopButton.IsEnabled = false;
        PauseButton.IsEnabled = false;
        PauseButton.Content = "Pause";
    }

    private async void PauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_countdownService == null) return;

        if (PauseButton.Content.ToString() == "Pause")
        {
            await _countdownService.PauseAsync();
            PauseButton.Content = "Resume";
        }
        else
        {
            await _countdownService.ResumeAsync();
            PauseButton.Content = "Pause";
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private CountdownConfig BuildConfig()
    {
        int.TryParse(HoursTextBox.Text, out var hours);
        int.TryParse(MinutesTextBox.Text, out var minutes);
        int.TryParse(SecondsTextBox.Text, out var seconds);

        var durationMs = (long)((hours * 3600 + minutes * 60 + seconds) * 1000);
        var fontFamily = (FontFamilyComboBox.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Segoe UI";

        var type = TypeComboBox.SelectedIndex switch
        {
            1 => CountdownType.CountdownToTime,
            2 => CountdownType.CountUp,
            _ => CountdownType.CountdownToZero
        };

        var config = new CountdownConfig
        {
            Type = type,
            DurationMs = durationMs,
            ShowOnAudience = AudienceCheckBox.IsChecked == true,
            ShowOnStage = StageCheckBox.IsChecked == true,
            FontFamily = fontFamily,
            FontColor = FontColorTextBox.Text,
            FontSize = 72
        };

        // For CountdownToTime, set target to now + duration
        if (type == CountdownType.CountdownToTime)
        {
            config.TargetTime = DateTime.Now.AddMilliseconds(durationMs);
        }

        return config;
    }
}
