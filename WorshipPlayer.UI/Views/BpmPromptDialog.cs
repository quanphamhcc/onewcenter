using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WorshipPlayer.UI.Views;

/// <summary>
/// Optional "I know the tempo" prompt shown at the start of Create Multi-Track. If the operator
/// knows the song's real BPM (and time signature), entering it here LOCKS the click grid spacing
/// to that tempo and lets the downbeat detector find beat 1 on a correct grid — instead of trusting
/// a possibly-wrong auto-detected tempo (e.g. a metrical mis-read like 107.67 for an 80 BPM song).
///
/// Leaving BPM blank = auto-detect (the previous behaviour). Built in code (no XAML) so it can be
/// shown from any window with a single static call.
///
/// Returns (bpm, timeSig): bpm is null when the operator left it blank (auto-detect); timeSig is the
/// chosen beats-per-bar (default 4). Returns null entirely if the operator cancels.
/// </summary>
public static class BpmPromptDialog
{
    public readonly record struct Result(double? Bpm, int TimeSig);

    /// <summary>
    /// Show the dialog. Returns the chosen (bpm, timeSig), or null if the operator cancels.
    /// </summary>
    public static Result? Show(Window? owner, string songTitle)
    {
        var win = new Window
        {
            Title = "Tempo (optional)",
            Width = 500,
            SizeToContent = SizeToContent.Height,
            WindowStartupLocation = owner != null
                ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            Owner = owner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x25, 0x25, 0x28)),
            WindowStyle = WindowStyle.SingleBorderWindow
        };

        var fg = new SolidColorBrush(Colors.White);
        var dim = new SolidColorBrush(Color.FromRgb(0xC0, 0xC0, 0xC6));

        var root = new StackPanel { Margin = new Thickness(20) };

        root.Children.Add(new TextBlock
        {
            Text = $"Do you know the tempo of \"{songTitle}\"?",
            Foreground = fg, FontSize = 16, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 10)
        });

        root.Children.Add(new TextBlock
        {
            Text = "If you know the real BPM, enter it to LOCK the click grid to that tempo and find "
                 + "beat 1 reliably. Leave it blank to auto-detect (the app will guess the tempo).",
            Foreground = dim, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 16)
        });

        // ── BPM row ──
        var bpmRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        bpmRow.Children.Add(new TextBlock
        {
            Text = "BPM:", Foreground = fg, Width = 100,
            VerticalAlignment = VerticalAlignment.Center
        });
        var bpmBox = new TextBox
        {
            Width = 100, Height = 26,
            VerticalContentAlignment = VerticalAlignment.Center,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1F)),
            Foreground = fg,
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x50, 0x50, 0x56))
        };
        bpmRow.Children.Add(bpmBox);
        bpmRow.Children.Add(new TextBlock
        {
            Text = "  (blank = auto-detect)", Foreground = dim, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        root.Children.Add(bpmRow);

        // ── Time signature row ──
        var tsRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };
        tsRow.Children.Add(new TextBlock
        {
            Text = "Beats per bar:", Foreground = fg, Width = 100,
            VerticalAlignment = VerticalAlignment.Center
        });
        var tsCombo = new ComboBox
        {
            Width = 100, Height = 26,
            Background = new SolidColorBrush(Color.FromRgb(0x1C, 0x1C, 0x1F)),
            Foreground = new SolidColorBrush(Colors.Black)
        };
        foreach (var n in new[] { 2, 3, 4, 5, 6, 7 })
            tsCombo.Items.Add(new ComboBoxItem { Content = $"{n}/4", Tag = n });
        tsCombo.SelectedIndex = 2; // default 4/4
        tsRow.Children.Add(tsCombo);
        tsRow.Children.Add(new TextBlock
        {
            Text = "  (4/4 is most common)", Foreground = dim, FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center
        });
        root.Children.Add(tsRow);

        var errorText = new TextBlock
        {
            Foreground = new SolidColorBrush(Color.FromRgb(0xF2, 0x6D, 0x6D)),
            FontSize = 12, Margin = new Thickness(0, 6, 0, 0),
            Visibility = Visibility.Collapsed
        };
        root.Children.Add(errorText);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var ok = new Button { Content = "Continue", Width = 110, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 90, Height = 30, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        win.Content = root;

        double? chosenBpm = null;
        int chosenTs = 4;

        ok.Click += (_, _) =>
        {
            string raw = bpmBox.Text?.Trim() ?? "";
            if (raw.Length == 0)
            {
                chosenBpm = null; // blank = auto-detect
            }
            else if (double.TryParse(raw,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double v)
                || double.TryParse(raw, out v))
            {
                if (v < 20 || v > 300)
                {
                    errorText.Text = "BPM must be between 20 and 300 (or blank for auto-detect).";
                    errorText.Visibility = Visibility.Visible;
                    return;
                }
                chosenBpm = v;
            }
            else
            {
                errorText.Text = "Enter a number for BPM, or leave it blank to auto-detect.";
                errorText.Visibility = Visibility.Visible;
                return;
            }

            chosenTs = (tsCombo.SelectedItem as ComboBoxItem)?.Tag is int t ? t : 4;
            win.DialogResult = true;
        };

        var result = win.ShowDialog();
        return result == true ? new Result(chosenBpm, chosenTs) : (Result?)null;
    }
}
