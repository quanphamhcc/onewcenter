// ─────────────────────────────────────────────────────────────────────────────
// STUB NDI OUTPUT IMPLEMENTATION
// 
// Replace with real NDI SDK implementation. Requires NewTek NDI SDK native DLL.
// 
// To use the real NDI SDK:
// 1. Download NDI SDK from https://ndi.video/for-developers/ndi-sdk/
// 2. Install the native DLL (Processing.NDI.Lib.x64.dll) alongside your app
// 3. Reference a .NET NDI wrapper (e.g., NewTek.NDI.Net or build your own P/Invoke layer)
// 4. Implement INdiOutput using NDIlib_send_create, NDIlib_send_send_video_v2, etc.
// 
// This stub logs operations and counts frames for diagnostics, but does not
// actually transmit any data over the network.
// ─────────────────────────────────────────────────────────────────────────────

using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Stub implementation of INdiOutput for development and testing.
/// All methods are no-ops that log diagnostic messages.
/// Used as a placeholder until the real NDI SDK native library is installed.
/// </summary>
public class StubNdiOutput : INdiOutput
{
    private bool _isActive;
    private string _sourceName = "";
    private bool _isInitialized;
    private long _frameCount;
    private readonly object _lock = new();

    /// <inheritdoc />
    public bool IsActive => _isActive;

    /// <inheritdoc />
    public string SourceName => _sourceName;

    /// <summary>
    /// Total number of frames passed to SendFrame (for diagnostics).
    /// </summary>
    public long FrameCount => _frameCount;

    /// <summary>
    /// Whether Initialize has been called successfully.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <inheritdoc />
    public bool Initialize(string sourceName)
    {
        lock (_lock)
        {
            _sourceName = sourceName;
            _isInitialized = true;
            _frameCount = 0;

            System.Diagnostics.Debug.WriteLine(
                $"[StubNdiOutput] Initialized with source name: '{sourceName}' " +
                "(stub - no real NDI SDK connected)");

            return true;
        }
    }

    /// <inheritdoc />
    public void Start()
    {
        lock (_lock)
        {
            if (!_isInitialized)
            {
                System.Diagnostics.Debug.WriteLine(
                    "[StubNdiOutput] Warning: Start() called before Initialize()");
                return;
            }

            _isActive = true;
            System.Diagnostics.Debug.WriteLine(
                $"[StubNdiOutput] Started NDI sender '{_sourceName}' (stub - no frames will be transmitted)");
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        lock (_lock)
        {
            _isActive = false;
            System.Diagnostics.Debug.WriteLine(
                $"[StubNdiOutput] Stopped NDI sender '{_sourceName}'. Total frames: {_frameCount}");
        }
    }

    /// <inheritdoc />
    public void SendFrame(byte[] bgraPixels, int width, int height, int stride)
    {
        if (!_isActive) return;

        Interlocked.Increment(ref _frameCount);

        // Log every 100th frame to avoid spamming output
        if (_frameCount % 100 == 0)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[StubNdiOutput] Frame #{_frameCount} ({width}x{height}, stride={stride}, " +
                $"data={bgraPixels.Length} bytes) - discarded (stub)");
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
        _isInitialized = false;
        System.Diagnostics.Debug.WriteLine("[StubNdiOutput] Disposed");
    }
}
