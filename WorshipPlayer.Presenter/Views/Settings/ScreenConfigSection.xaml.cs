using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Settings;

/// <summary>
/// Screen configuration section — detect monitors and assign purpose (Audience/Stage).
/// Uses Win32 EnumDisplayMonitors for detection (no WinForms dependency).
/// </summary>
public partial class ScreenConfigSection : UserControl
{
    private List<MonitorInfo> _monitors = new();

    // ─── Đợt 2 T16 / R8 — audience outputs list ───────────────────────────
    // Working copies (deep) of the configured outputs; written back on Save.
    private readonly List<OutputConfig> _outputs = new();
    private List<SlideTemplate> _templates = new();
    private readonly List<RadioButton> _outputRadios = new();

    /// <summary>
    /// Template service used to populate the per-output template-override dropdown.
    /// Set by SettingsWindow (same pattern as BiblesSection.BibleProvider).
    /// </summary>
    public ITemplateService? TemplateService { get; set; }

    /// <summary>
    /// Selected stage monitor index (-1 = none/use web).
    /// </summary>
    public int StageDisplayIndex { get; private set; } = -1;

    public ScreenConfigSection()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            DetectAndPopulateMonitors();
            _ = LoadTemplatesAsync();
        };
    }

    /// <summary>
    /// Set the current display configuration (called by SettingsWindow before display).
    /// </summary>
    public void SetCurrentConfig(int stageIndex, List<OutputConfig> outputs)
    {
        StageDisplayIndex = stageIndex;
        _outputs.Clear();
        foreach (var o in outputs)
            _outputs.Add(CopyOutput(o));
        RefreshOutputRows();
    }

    /// <summary>
    /// The edited outputs (deep copies) for SettingsWindow to persist on Save.
    /// </summary>
    public List<OutputConfig> GetOutputs()
    {
        return _outputs.Select(CopyOutput).ToList();
    }

    private static OutputConfig CopyOutput(OutputConfig src) => new()
    {
        Id = src.Id,
        Name = src.Name,
        DisplayIndex = src.DisplayIndex,
        Purpose = src.Purpose,
        ForegroundTemplateIdOverride = src.ForegroundTemplateIdOverride,
        IsEnabled = src.IsEnabled,
    };

    private async Task LoadTemplatesAsync()
    {
        try
        {
            if (TemplateService == null) return;
            _templates = await TemplateService.GetAllAsync();
            RefreshOutputRows();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[ScreenConfigSection] LoadTemplates failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Rebuild the per-output rows (enable checkbox, name, monitor combo, template
    /// override combo). Called after monitor detection, template load, add/remove.
    /// </summary>
    private void RefreshOutputRows()
    {
        OutputsStack.Children.Clear();
        _outputRadios.Clear();

        foreach (var output in _outputs)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            // Line 1: select radio + enabled checkbox + name
            var header = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            var radio = new RadioButton
            {
                GroupName = "T16OutputSelection",
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                ToolTip = "Select for removal",
            };
            var enabled = new CheckBox
            {
                Content = "Enabled",
                IsChecked = output.IsEnabled,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };
            enabled.Checked += (_, _) => output.IsEnabled = true;
            enabled.Unchecked += (_, _) => output.IsEnabled = false;
            var name = new TextBlock
            {
                Text = output.Purpose == ScreenPurpose.Audience ? output.Name : $"{output.Name} (Stage — no window)",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(8, 0, 0, 0),
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
            };
            header.Children.Add(radio);
            header.Children.Add(enabled);
            header.Children.Add(name);
            _outputRadios.Add(radio);

            // Line 2: monitor combo + template override combo
            var controls = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(26, 0, 0, 0) };

            var displayCombo = new ComboBox { Width = 230, Margin = new Thickness(0, 0, 8, 0) };
            displayCombo.Items.Add("(None - disabled)");
            for (int i = 0; i < _monitors.Count; i++)
            {
                var m = _monitors[i];
                displayCombo.Items.Add($"Monitor {i + 1}: {m.Name} ({m.Width}x{m.Height}){(m.IsPrimary ? " [Primary]" : "")}");
            }
            displayCombo.SelectedIndex = Math.Clamp(output.DisplayIndex + 1, 0, displayCombo.Items.Count - 1);
            displayCombo.SelectionChanged += (_, _) =>
            {
                if (displayCombo.SelectedIndex >= 0)
                    output.DisplayIndex = displayCombo.SelectedIndex - 1;
            };

            // Template override dropdown — neutral wording per the R8 mapping note:
            // the "Override Foreground Template" label of WorshipTools is NOT verified,
            // so the null option reads "(Không ghi đè)".
            var templateCombo = new ComboBox { Width = 230 };
            templateCombo.Items.Add(new ComboBoxItem { Content = "(Không ghi đè)", Tag = null });
            foreach (var t in _templates)
                templateCombo.Items.Add(new ComboBoxItem { Content = t.Name, Tag = t.Id });
            templateCombo.SelectedIndex = 0;
            if (!string.IsNullOrWhiteSpace(output.ForegroundTemplateIdOverride))
            {
                for (int i = 0; i < templateCombo.Items.Count; i++)
                {
                    if (templateCombo.Items[i] is ComboBoxItem item &&
                        item.Tag as string == output.ForegroundTemplateIdOverride)
                    {
                        templateCombo.SelectedIndex = i;
                        break;
                    }
                }
            }
            templateCombo.SelectionChanged += (_, _) =>
            {
                if (templateCombo.SelectedItem is ComboBoxItem selected)
                    output.ForegroundTemplateIdOverride = selected.Tag as string;
            };

            controls.Children.Add(displayCombo);

            var templateLabel = new TextBlock
            {
                Text = "Template override:",
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0),
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(139, 146, 165)),
            };
            controls.Children.Add(templateLabel);
            controls.Children.Add(templateCombo);

            row.Children.Add(header);
            row.Children.Add(controls);
            OutputsStack.Children.Add(row);
        }
    }

    private void AddOutputButton_Click(object sender, RoutedEventArgs e)
    {
        _outputs.Add(new OutputConfig
        {
            Name = $"Output {_outputs.Count + 1}",
            Purpose = ScreenPurpose.Audience,
            DisplayIndex = -1,
            IsEnabled = true,
            ForegroundTemplateIdOverride = null,
        });
        RefreshOutputRows();
    }

    private void RemoveOutputButton_Click(object sender, RoutedEventArgs e)
    {
        for (int i = 0; i < _outputRadios.Count && i < _outputs.Count; i++)
        {
            if (_outputRadios[i].IsChecked == true)
            {
                _outputs.RemoveAt(i);
                RefreshOutputRows();
                return;
            }
        }
    }

    private void DetectAndPopulateMonitors()
    {
        _monitors = DetectMonitors();
        PopulateDropdowns();
        RefreshOutputRows();
    }

    private void PopulateDropdowns()
    {
        StageMonitorCombo.Items.Clear();

        StageMonitorCombo.Items.Add("(None - use web stage display)");

        string monitorList = "";
        for (int i = 0; i < _monitors.Count; i++)
        {
            var m = _monitors[i];
            string label = $"Monitor {i + 1}: {m.Name} ({m.Width}x{m.Height}){(m.IsPrimary ? " [Primary]" : "")}";
            StageMonitorCombo.Items.Add(label);
            monitorList += $"{label}\n";
        }

        MonitorListText.Text = _monitors.Count > 0 ? monitorList.TrimEnd() : "No monitors detected. Click Refresh.";

        // Set selections (index 0 = None, index 1 = monitor 0, etc.)
        StageMonitorCombo.SelectedIndex = Math.Clamp(StageDisplayIndex + 1, 0, StageMonitorCombo.Items.Count - 1);
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
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };

            // Monitor name below
            var nameText = new System.Windows.Controls.TextBlock
            {
                Text = $"{monitor.Name} ({monitor.Width}x{monitor.Height})",
                FontSize = 24,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(139, 146, 165)), // Secondary text
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
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
