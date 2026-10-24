using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.UI.Views;

/// <summary>
/// Analyze-first dialog for the click grid. Shows the detected tempo and the analyzer's verdict
/// (constant vs variable tempo) with the fit metrics, and lets the operator choose how the click
/// grid is built: Auto (recommended), Even, or Tempo-following. Built in code (no XAML) so it can
/// be shown from any window with a single static call.
///
/// Returned mode strings match the Python --grid-mode values: "auto" | "even" | "following".
/// Returns null if the operator cancels.
/// </summary>
public static class GridModeDialog
{
    /// <summary>
    /// Show the dialog. <paramref name="fit"/> carries the analyzer's verdict/metrics; the
    /// recommended option is preselected from it. Returns the chosen mode, or null on cancel.
    /// </summary>
    public static string? Show(Window? owner, string songTitle,
        PythonAnalysisService.GridFit fit, double detectedBpm)
    {
        bool constant = fit.EvenFits;
        string recommended = string.IsNullOrEmpty(fit.Recommended)
            ? (constant ? "even" : "following")
            : fit.Recommended;

        var win = new Window
        {
            Title = "Click Grid",
            Width = 540,
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
            Text = $"Tempo analysis for \"{songTitle}\"",
            Foreground = fg, FontSize = 16, FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Bpm to show: prefer the fit BPM when present.
        double bpm = fit.FitBpm > 0 ? fit.FitBpm : detectedBpm;
        // Convert very high values to the felt quarter-note tempo for display clarity.
        string bpmNote = bpm >= 150 ? $"{bpm:F1} BPM (≈ {bpm / 2:F0} quarter-note)" : $"{bpm:F1} BPM";

        string verdict = constant ? "CONSTANT tempo — an even grid fits the whole song"
                                   : "VARIABLE tempo — the band pushes/pulls over time";
        var verdictColor = constant
            ? new SolidColorBrush(Color.FromRgb(0x7E, 0xD3, 0x21))
            : new SolidColorBrush(Color.FromRgb(0xF5, 0xA6, 0x23));

        root.Children.Add(new TextBlock { Text = $"Detected tempo:  {bpmNote}", Foreground = dim, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(new TextBlock { Text = verdict, Foreground = verdictColor, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        root.Children.Add(new TextBlock
        {
            Text = $"Even-grid deviation: max {fit.FitMaxDevMs:F0} ms  (raw beat jitter {fit.RawJitterMs:F0} ms)",
            Foreground = dim, FontSize = 12, Margin = new Thickness(0, 0, 0, 14)
        });

        root.Children.Add(new TextBlock
        {
            Text = "How should the click grid be built?",
            Foreground = fg, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8)
        });

        RadioButton MakeOption(string title, string detail)
        {
            var rb = new RadioButton
            {
                Foreground = fg,
                Margin = new Thickness(0, 4, 0, 4),
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock { Text = title, Foreground = fg, FontWeight = FontWeights.SemiBold },
                        new TextBlock { Text = detail, Foreground = dim, FontSize = 12, TextWrapping = TextWrapping.Wrap }
                    }
                }
            };
            return rb;
        }

        string recName = recommended == "even" ? "Even grid" : "Tempo-following";
        var optAuto = MakeOption($"Auto  (recommended: {recName})",
            "Let the app decide from the analysis above.");
        var optEven = MakeOption("Even grid",
            "Mathematically steady click. Best for constant-tempo (studio/programmed) songs; a live band can lock to it.");
        var optFollow = MakeOption("Tempo-following",
            "Click tracks the band's timing. Best for live/variable songs; stays on the beat instead of drifting.");

        optAuto.IsChecked = true; // default to Auto
        root.Children.Add(optAuto);
        root.Children.Add(optEven);
        root.Children.Add(optFollow);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 16, 0, 0)
        };
        var ok = new Button { Content = "Generate Click", Width = 130, Height = 30, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
        var cancel = new Button { Content = "Cancel", Width = 90, Height = 30, IsCancel = true };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        root.Children.Add(buttons);

        win.Content = root;

        string? chosen = null;
        ok.Click += (_, _) =>
        {
            chosen = optEven.IsChecked == true ? "even"
                   : optFollow.IsChecked == true ? "following"
                   : "auto";
            win.DialogResult = true;
        };

        var result = win.ShowDialog();
        return result == true ? chosen : null;
    }
}
