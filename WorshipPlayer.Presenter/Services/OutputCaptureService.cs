using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Captures a WPF visual (e.g., OutputWindow content) as bitmap frames at a configurable FPS.
/// Uses RenderTargetBitmap to render the visual to a BGRA byte array, then fires
/// the FrameCaptured event for consumers (such as NdiOutputService).
/// 
/// Designed to run on the WPF UI thread via DispatcherTimer.
/// </summary>
public class OutputCaptureService : IDisposable
{
    private DispatcherTimer? _timer;
    private FrameworkElement? _target;
    private int _width;
    private int _height;
    private bool _isCapturing;

    /// <summary>
    /// Whether the capture service is currently running.
    /// </summary>
    public bool IsCapturing => _isCapturing;

    /// <summary>
    /// Current capture FPS.
    /// </summary>
    public int Fps { get; private set; }

    /// <summary>
    /// Capture width in pixels.
    /// </summary>
    public int Width => _width;

    /// <summary>
    /// Capture height in pixels.
    /// </summary>
    public int Height => _height;

    /// <summary>
    /// Fired each time a frame is captured.
    /// Contains the BGRA pixel data and dimensions.
    /// </summary>
    public event EventHandler<FrameCapturedEventArgs>? FrameCaptured;

    /// <summary>
    /// Start capturing frames from the target visual at the specified FPS and resolution.
    /// </summary>
    /// <param name="target">The WPF FrameworkElement to capture (e.g., OutputWindow root grid).</param>
    /// <param name="fps">Frames per second to capture (e.g., 30).</param>
    /// <param name="width">Output width in pixels.</param>
    /// <param name="height">Output height in pixels.</param>
    public void Start(FrameworkElement target, int fps, int width, int height)
    {
        if (_isCapturing)
            Stop();

        _target = target;
        Fps = Math.Clamp(fps, 1, 60);
        _width = Math.Max(width, 1);
        _height = Math.Max(height, 1);

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(1000.0 / Fps)
        };
        _timer.Tick += OnTimerTick;
        _timer.Start();

        _isCapturing = true;
    }

    /// <summary>
    /// Stop capturing frames.
    /// </summary>
    public void Stop()
    {
        if (_timer != null)
        {
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }

        _isCapturing = false;
        _target = null;
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_target == null || !_isCapturing) return;

        try
        {
            CaptureFrame();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[OutputCaptureService] Frame capture error: {ex.Message}");
        }
    }

    private void CaptureFrame()
    {
        if (_target == null) return;

        // Determine the DPI scaling of the target
        var source = PresentationSource.FromVisual(_target);
        double dpiX = 96.0;
        double dpiY = 96.0;
        if (source?.CompositionTarget != null)
        {
            dpiX = 96.0 * source.CompositionTarget.TransformToDevice.M11;
            dpiY = 96.0 * source.CompositionTarget.TransformToDevice.M22;
        }

        // Calculate the scale factor to render at the desired resolution
        double scaleX = _width / _target.ActualWidth;
        double scaleY = _height / _target.ActualHeight;

        if (_target.ActualWidth <= 0 || _target.ActualHeight <= 0)
            return;

        // Create a RenderTargetBitmap at the desired resolution
        var renderBitmap = new RenderTargetBitmap(
            _width, _height, dpiX * scaleX, dpiY * scaleY, PixelFormats.Pbgra32);

        renderBitmap.Render(_target);

        // Copy pixels to byte array
        int stride = _width * 4; // 4 bytes per pixel (BGRA)
        byte[] pixels = new byte[stride * _height];
        renderBitmap.CopyPixels(pixels, stride, 0);

        // Fire event
        FrameCaptured?.Invoke(this, new FrameCapturedEventArgs
        {
            BgraPixels = pixels,
            Width = _width,
            Height = _height,
            Stride = stride
        });
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Stop();
    }
}

/// <summary>
/// Event args containing a captured frame's pixel data.
/// </summary>
public class FrameCapturedEventArgs : EventArgs
{
    /// <summary>
    /// Raw pixel data in BGRA (Blue, Green, Red, Alpha) format.
    /// </summary>
    public byte[] BgraPixels { get; set; } = Array.Empty<byte>();

    /// <summary>
    /// Frame width in pixels.
    /// </summary>
    public int Width { get; set; }

    /// <summary>
    /// Frame height in pixels.
    /// </summary>
    public int Height { get; set; }

    /// <summary>
    /// Number of bytes per row (typically Width * 4).
    /// </summary>
    public int Stride { get; set; }
}
