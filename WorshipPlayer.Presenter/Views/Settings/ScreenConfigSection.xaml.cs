using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;

namespace WorshipPlayer.Presenter.Views.Settings;

/// <summary>
/// Screen configuration section — detect monitors and assign purpose (Audience/Stage).
/// Uses Win32 EnumDisplayMonitors for detection (no WinForms dependency).
/// </summary>
public partial class ScreenConfigSection : UserControl
{
    private List<MonitorInfo> _monitors = new();

    /// <summary>
    /// Selected audience monitor index (-1 = none).
    /// </summary>
    public int AudienceDisplayIndex { get; private set; } = -1;

    /// <summary>
    /// Selected stage monitor index (-1 = none/use web).
    /// </summary>
    public int StageDisplayIndex { get; private set; } = -1;

    public ScreenConfigSection()
    {
        InitializeComponent();
        Loaded += (_, _) => DetectAndPopulateMonitors();
    }

    /// <summary>
    /// Set the current display configuration (called by SettingsWindow before display).
    /// </summary>
    public void SetCurrentConfig(int audienceIndex, int stageIndex)
    {
        AudienceDisplayIndex = audienceIndex;
        StageDisplayIndex = stageIndex;
    }

    private void DetectAndPopulateMonitors()
    {
        _monitors = DetectMonitors();
        PopulateDropdowns();
    }

    private void PopulateDropdowns()
    {
        AudienceMonitorCombo.Items.Clear();
        StageMonitorCombo.Items.Clear();

        AudienceMonitorCombo.Items.Add("(None - disabled)");
        StageMonitorCombo.Items.Add("(None - use web stage display)");

        string monitorList = "";
        for (int i = 0; i < _monitors.Count; i++)
        {
            var m = _monitors[i];
            string label = $"Monitor {i + 1}: {m.Name} ({m.Width}x{m.Height}){(m.IsPrimary ? " [Primary]" : "")}";
            AudienceMonitorCombo.Items.Add(label);
            StageMonitorCombo.Items.Add(label);
            monitorList += $"{label}\n";
        }

        MonitorListText.Text = _monitors.Count > 0 ? monitorList.TrimEnd() : "No monitors detected. Click Refresh.";

        // Set selections (index 0 = None, index 1 = monitor 0, etc.)
        AudienceMonitorCombo.SelectedIndex = Math.Clamp(AudienceDisplayIndex + 1, 0, AudienceMonitorCombo.Items.Count - 1);
        StageMonitorCombo.SelectedIndex = Math.Clamp(StageDisplayIndex + 1, 0, StageMonitorCombo.Items.Count - 1);
    }

    private void AudienceMonitor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        AudienceDisplayIndex = AudienceMonitorCombo.SelectedIndex - 1;
    }

    private void StageMonitor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        StageDisplayIndex = StageMonitorCombo.SelectedIndex - 1;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        DetectAndPopulateMonitors();
    }

    // ─── Identify Monitors (number overlays) ───────────────────────────

    private List<Window> _identifyWindows = new();
    private bool _isIdentifying = false;

    private void IdentifyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isIdentifying)
        {
            HideIdentifyOverlays();
        }
        else
        {
            ShowIdentifyOverlays();
        }
    }

    private void ShowIdentifyOverlays()
    {
        HideIdentifyOverlays(); // Clear any existing

        for (int i = 0; i < _monitors.Count; i++)
        {
            var monitor = _monitors[i];
            var overlay = new Window
            {
                Title = $"Monitor {i + 1}",
                WindowStyle = WindowStyle.None,
                AllowsTransparency = false,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(15, 17, 23)), // Solid dark (no transparency issues)
                Topmost = true,
                ShowInTaskbar = true, // Show in taskbar so user can find/close them
                ResizeMode = ResizeMode.NoResize,
                Left = monitor.Left,
                Top = monitor.Top,
                Width = monitor.Width,
                Height = monitor.Height,
                WindowState = WindowState.Normal
            };

            var grid = new System.Windows.Controls.Grid();

            // Large number in center
            var numberText = new System.Windows.Controls.TextBlock
            {
                Text = (i + 1).ToString(),
                FontSize = Math.Min(monitor.Width, monitor.Height) * 0.4,
                FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0, 212, 255)), // Cyan
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            // Monitor name below
            var nameText = new System.Windows.Controls.TextBlock
            {
                Text = $"{monitor.Name} ({monitor.Width}x{monitor.Height})",
                FontSize = 24,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(139, 146, 165)), // Secondary text
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, 60)
            };

            grid.Children.Add(numberText);
            grid.Children.Add(nameText);
            overlay.Content = grid;

            overlay.Show();
            _identifyWindows.Add(overlay);
        }

        _isIdentifying = true;
        IdentifyButton.Content = "Hide Monitor Numbers";
    }

    private void HideIdentifyOverlays()
    {
        foreach (var window in _identifyWindows)
        {
            try { window.Close(); } catch { }
        }
        _identifyWindows.Clear();
        _isIdentifying = false;
        IdentifyButton.Content = "Show Monitor Numbers";
    }

    /// <summary>
    /// Clean up overlays if the section is unloaded (user navigates away in settings).
    /// </summary>
    protected override void OnVisualParentChanged(DependencyObject oldParent)
    {
        base.OnVisualParentChanged(oldParent);
        if (oldParent != null && _isIdentifying)
        {
            HideIdentifyOverlays();
        }
    }

    // ─── Win32 Monitor Detection (no WinForms dependency) ──────────────

    private static List<MonitorInfo> DetectMonitors()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var info = new MONITORINFOEX();
            info.cbSize = Marshal.SizeOf(info);
            if (GetMonitorInfo(hMonitor, ref info))
            {
                monitors.Add(new MonitorInfo
                {
                    Name = new string(info.szDevice).TrimEnd('\0'),
                    Width = info.rcMonitor.Right - info.rcMonitor.Left,
                    Height = info.rcMonitor.Bottom - info.rcMonitor.Top,
                    Left = info.rcMonitor.Left,
                    Top = info.rcMonitor.Top,
                    IsPrimary = (info.dwFlags & 1) != 0
                });
            }
            return true;
        }, IntPtr.Zero);
        return monitors;
    }

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }

    private class MonitorInfo
    {
        public string Name { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public int Left { get; set; }
        public int Top { get; set; }
        public bool IsPrimary { get; set; }
    }
}
