using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Manages output display windows (congregation output, stage display).
/// Handles multi-monitor detection and window placement.
/// 
/// This service is responsible for:
/// 1. Detecting available monitors
/// 2. Creating/positioning output windows on the correct screen
/// 3. Managing full-screen state
/// 4. Providing display information to the UI for configuration
/// </summary>
public class DisplayManager
{
    private readonly PresenterSettings _settings;

    public List<DisplayInfo> AvailableDisplays { get; private set; } = new();

    /// <summary>
    /// The currently active output display info.
    /// </summary>
    public DisplayInfo? OutputDisplay => _settings.OutputDisplay >= 0 && _settings.OutputDisplay < AvailableDisplays.Count
        ? AvailableDisplays[_settings.OutputDisplay]
        : null;

    /// <summary>
    /// The currently active stage display info.
    /// </summary>
    public DisplayInfo? StageDisplay => _settings.StageDisplay >= 0 && _settings.StageDisplay < AvailableDisplays.Count
        ? AvailableDisplays[_settings.StageDisplay]
        : null;

    public DisplayManager(PresenterSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Clamp a saved display index to the currently available monitors.
    /// Pure helper (no WPF/System.Windows dependency) so it is unit-testable
    /// and cross-platform. Returns -1 (unassigned) when the saved index cannot
    /// map to a real monitor:
    ///  - savedIndex &lt; 0 preserves "unassigned".
    ///  - monitorCount &lt;= 0 (no monitors) returns -1.
    ///  - savedIndex &gt;= monitorCount (monitor removed) returns -1.
    /// Otherwise the in-range index is preserved.
    /// </summary>
    public static int ClampDisplayIndex(int savedIndex, int monitorCount)
    {
        if (savedIndex < 0) return -1;
        if (monitorCount <= 0) return -1;
        if (savedIndex >= monitorCount) return -1;
        return savedIndex;
    }

    /// <summary>
    /// Refresh the list of available displays.
    /// Call this on startup and when monitors are connected/disconnected.
    /// </summary>
    public void RefreshDisplays()
    {
        AvailableDisplays.Clear();

        // This will be populated by the UI layer (WPF) which has access to
        // System.Windows.Forms.Screen or WPF's SystemParameters.
        // The service just holds the data; the UI populates it.
    }

    /// <summary>
    /// Add a detected display (called by UI layer during initialization).
    /// </summary>
    public void RegisterDisplay(DisplayInfo display)
    {
        AvailableDisplays.Add(display);
    }

    /// <summary>
    /// Get the bounds for the output window placement.
    /// </summary>
    public DisplayInfo? GetOutputBounds()
    {
        if (_settings.OutputDisplay < 0 || _settings.OutputDisplay >= AvailableDisplays.Count)
            return null;

        return AvailableDisplays[_settings.OutputDisplay];
    }

    /// <summary>
    /// Get the bounds for the stage display window placement.
    /// </summary>
    public DisplayInfo? GetStageBounds()
    {
        if (_settings.StageDisplay < 0 || _settings.StageDisplay >= AvailableDisplays.Count)
            return null;

        return AvailableDisplays[_settings.StageDisplay];
    }
}

/// <summary>
/// Information about a display/monitor.
/// </summary>
public class DisplayInfo
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public double Left { get; set; }
    public double Top { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
    public bool IsPrimary { get; set; }

    public override string ToString()
    {
        return $"{Name} ({Width}x{Height}){(IsPrimary ? " [Primary]" : "")}";
    }
}
