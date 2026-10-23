using LibVLCSharp.Shared;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Manages motion background video playback using LibVLC.
/// Handles looping video backgrounds displayed behind lyrics.
/// The WPF layer applies opacity via the VideoView control.
/// </summary>
public class VideoBackgroundService : IDisposable
{
    // The LibVLC native core must be initialized exactly ONCE per process, before any
    // LibVLC instance is constructed. OutputWindow is created multiple times (recreated after
    // close), so each window builds its own LibVLC/MediaPlayer, but Core.Initialize() runs only once.
    private static readonly object _coreInitLock = new();
    private static bool _coreInitialized;

    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private bool _isInitialized;
    private string _currentVideoPath = "";
    private double _opacity = 0.5;
    private bool _shouldLoop;

    /// <summary>
    /// Whether the service has been initialized with a LibVLC instance.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Whether a background video is currently playing.
    /// </summary>
    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;

    /// <summary>
    /// The path of the currently loaded/playing video.
    /// </summary>
    public string CurrentVideoPath => _currentVideoPath;

    /// <summary>
    /// The opacity value for the video background (0.0 - 1.0).
    /// Applied by the WPF layer on the VideoView element.
    /// </summary>
    public double Opacity
    {
        get => _opacity;
        private set => _opacity = Math.Clamp(value, 0.0, 1.0);
    }

    /// <summary>
    /// The underlying LibVLC instance (shared with VideoPlaybackService).
    /// </summary>
    public LibVLC? LibVLCInstance => _libVLC;

    /// <summary>
    /// The MediaPlayer instance for binding to a VideoView.
    /// </summary>
    public MediaPlayer? Player => _mediaPlayer;

    /// <summary>
    /// Fired when a NON-looping background video reaches its end (đợt 2 T4 / R3).
    /// Looping videos restart instead and never raise this. Raised on a LibVLC
    /// thread — subscribers must dispatch to their own thread before touching UI
    /// (same contract as the player's EndReached event).
    /// </summary>
    public event EventHandler? PlaybackEnded;

    /// <summary>
    /// Initialize the LibVLC core and create a media player instance.
    /// This is safe to call — if LibVLC native libraries are not available,
    /// the service will remain uninitialized and video features will be disabled.
    /// </summary>
    public void Initialize()
    {
        if (_isInitialized) return;

        try
        {
            // Load the native libvlc core exactly once per process before constructing any LibVLC.
            lock (_coreInitLock)
            {
                if (!_coreInitialized)
                {
                    LibVLCSharp.Shared.Core.Initialize();
                    _coreInitialized = true;
                }
            }

            _libVLC = new LibVLC("--no-audio", "--video-title=MotionBackground");
            _mediaPlayer = new MediaPlayer(_libVLC);
            _isInitialized = true;
        }
        catch (Exception ex)
        {
            // LibVLC native libraries not found or failed to load.
            // Video background features will be disabled but the app continues.
            System.Diagnostics.Debug.WriteLine($"[VideoBackgroundService] LibVLC initialization failed (video features disabled): {ex.Message}");
            _libVLC = null;
            _mediaPlayer = null;
            _isInitialized = false;
        }
    }

    /// <summary>
    /// Play a video file as a looping motion background.
    /// </summary>
    /// <param name="videoPath">Full path to the video file (MP4, MOV, WebM, etc.).</param>
    /// <param name="loop">Whether to loop the video continuously (default: true).</param>
    public void PlayBackground(string videoPath, bool loop = true)
    {
        if (!_isInitialized || _libVLC == null || _mediaPlayer == null)
            return;

        if (string.IsNullOrEmpty(videoPath))
            return;

        StopBackground();

        _shouldLoop = loop;
        _currentVideoPath = videoPath;

        var media = new Media(_libVLC, new Uri(videoPath));

        if (loop)
        {
            media.AddOption("input-repeat=65535"); // Loop many times (effectively infinite)
        }

        // Subscribe to EndReached for deterministic infinite looping
        // (handles cases where input-repeat is not honored by the codec/platform)
        _mediaPlayer.EndReached += OnMediaEndReached;

        _mediaPlayer.Play(media);
    }

    /// <summary>
    /// Handles EndReached: for a looping video, restart it for deterministic infinite
    /// looping; for a non-looping video this is a genuine end — surface it via
    /// <see cref="PlaybackEnded"/> so the auto-advance VideoEnd trigger can fire (đợt 2 T4).
    /// LibVLC fires EndReached on a VLC thread, so we must dispatch Play on a ThreadPool thread
    /// (calling Play directly in the EndReached handler deadlocks).
    /// </summary>
    private void OnMediaEndReached(object? sender, EventArgs e)
    {
        if (!_shouldLoop)
        {
            // Genuine end of a play-once video. Clear the path so a stale replay of
            // the same handler cannot fire twice for one playback.
            _currentVideoPath = "";
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
            return;
        }

        if (_libVLC == null || _mediaPlayer == null)
            return;

        var path = _currentVideoPath;
        if (string.IsNullOrEmpty(path))
            return;

        // Must not call Play from within the EndReached handler (LibVLC deadlocks)
        ThreadPool.QueueUserWorkItem(_ =>
        {
            try
            {
                if (_mediaPlayer == null || _libVLC == null || !_shouldLoop)
                    return;

                var media = new Media(_libVLC, new Uri(path));
                media.AddOption("input-repeat=65535");
                _mediaPlayer.Play(media);
            }
            catch
            {
                // Ignore errors during loop restart (e.g., disposed)
            }
        });
    }

    /// <summary>
    /// Stop the current background video playback.
    /// </summary>
    public void StopBackground()
    {
        if (_mediaPlayer == null) return;

        _shouldLoop = false;
        _mediaPlayer.EndReached -= OnMediaEndReached;

        if (_mediaPlayer.IsPlaying)
        {
            _mediaPlayer.Stop();
        }

        _currentVideoPath = "";
    }

    /// <summary>
    /// Set the opacity value for the video background.
    /// The actual opacity is applied by the WPF layer on the VideoView element.
    /// </summary>
    /// <param name="opacity">Opacity value between 0.0 (transparent) and 1.0 (opaque).</param>
    public void SetOpacity(double opacity)
    {
        Opacity = opacity;
    }

    /// <summary>
    /// Release all LibVLC resources.
    /// </summary>
    public void Dispose()
    {
        StopBackground();

        _mediaPlayer?.Dispose();
        _mediaPlayer = null;

        _libVLC?.Dispose();
        _libVLC = null;

        _isInitialized = false;
    }
}
