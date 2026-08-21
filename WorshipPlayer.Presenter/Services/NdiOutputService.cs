using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Service that wraps the INdiOutput interface and manages the NDI sender lifecycle.
/// 
/// If no INdiOutput implementation is provided (e.g., NDI SDK not installed),
/// the service logs a warning and operates in a degraded mode where all operations
/// are no-ops. This ensures the application always starts cleanly.
/// </summary>
public class NdiOutputService : IDisposable
{
    private readonly PresenterSettings _settings;
    private INdiOutput? _ndiOutput;
    private bool _isInitialized;

    /// <summary>
    /// Whether the NDI output is currently active and sending frames.
    /// </summary>
    public bool IsActive => _ndiOutput?.IsActive ?? false;

    /// <summary>
    /// The NDI source name being broadcast.
    /// </summary>
    public string SourceName => _ndiOutput?.SourceName ?? _settings.NdiSourceName;

    /// <summary>
    /// Whether an NDI output implementation has been provided.
    /// </summary>
    public bool HasOutput => _ndiOutput != null;

    /// <summary>
    /// Event raised when the NDI output state changes (started/stopped).
    /// </summary>
    public event EventHandler<NdiStateChangedEventArgs>? StateChanged;

    public NdiOutputService(PresenterSettings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// Set the NDI output implementation. If null, the service operates in stub mode.
    /// </summary>
    public void SetNdiOutput(INdiOutput? output)
    {
        _ndiOutput?.Dispose();
        _ndiOutput = output;
        _isInitialized = false;
    }

    /// <summary>
    /// Initialize the NDI sender with the configured source name.
    /// </summary>
    /// <returns>True if initialization succeeded or if running in degraded mode.</returns>
    public bool Initialize()
    {
        if (_ndiOutput == null)
        {
            System.Diagnostics.Debug.WriteLine(
                "[NdiOutputService] No NDI output implementation provided. " +
                "NDI features will be unavailable. Install NDI SDK for full functionality.");
            return false;
        }

        string sourceName = !string.IsNullOrWhiteSpace(_settings.NdiSourceName)
            ? _settings.NdiSourceName
            : "OWC Presenter Output";

        _isInitialized = _ndiOutput.Initialize(sourceName);
        return _isInitialized;
    }

    /// <summary>
    /// Start the NDI sender. Initializes first if not already done.
    /// </summary>
    public void Start()
    {
        if (_ndiOutput == null)
        {
            System.Diagnostics.Debug.WriteLine(
                "[NdiOutputService] Cannot start - no NDI output implementation available.");
            return;
        }

        if (!_isInitialized)
        {
            if (!Initialize())
                return;
        }

        _ndiOutput.Start();
        StateChanged?.Invoke(this, new NdiStateChangedEventArgs { IsActive = true });
    }

    /// <summary>
    /// Stop the NDI sender.
    /// </summary>
    public void Stop()
    {
        if (_ndiOutput == null) return;

        _ndiOutput.Stop();
        StateChanged?.Invoke(this, new NdiStateChangedEventArgs { IsActive = false });
    }

    /// <summary>
    /// Send a video frame to NDI receivers.
    /// </summary>
    /// <param name="bgraPixels">Raw pixel data in BGRA format.</param>
    /// <param name="width">Frame width in pixels.</param>
    /// <param name="height">Frame height in pixels.</param>
    public void SendFrame(byte[] bgraPixels, int width, int height)
    {
        if (_ndiOutput == null || !_ndiOutput.IsActive) return;

        int stride = width * 4; // BGRA = 4 bytes per pixel
        _ndiOutput.SendFrame(bgraPixels, width, height, stride);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _ndiOutput?.Dispose();
        _ndiOutput = null;
        _isInitialized = false;
    }
}

/// <summary>
/// Event args for NDI state changes.
/// </summary>
public class NdiStateChangedEventArgs : EventArgs
{
    public bool IsActive { get; set; }
}
