using LibVLCSharp.Shared;

namespace WorshipPlayer.Presenter.Services;

/// <summary>
/// Full video playback service for 'Video' service items.
/// Uses a shared LibVLC instance (from VideoBackgroundService) for efficiency.
/// Supports play, pause, resume, stop, seek, and position tracking.
/// </summary>
public class VideoPlaybackService : IDisposable
{
    private LibVLC? _libVLC;
    private MediaPlayer? _mediaPlayer;
    private bool _isInitialized;

    /// <summary>
    /// Fired when video playback reaches the end.
    /// </summary>
    public event EventHandler? PlaybackEnded;

    /// <summary>
    /// Fired periodically with the current playback position in milliseconds.
    /// </summary>
    public event EventHandler<double>? PositionChanged;

    /// <summary>
    /// Whether the service has been initialized.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Whether video is currently playing (not paused, not stopped).
    /// </summary>
    public bool IsPlaying => _mediaPlayer?.IsPlaying ?? false;

    /// <summary>
    /// Whether video is currently paused.
    /// </summary>
    public bool IsPaused { get; private set; }

    /// <summary>
    /// Total duration of the current media in milliseconds.
    /// </summary>
    public double Duration => _mediaPlayer?.Length ?? 0;

    /// <summary>
    /// Current playback position in milliseconds.
    /// </summary>
    public double Position => (_mediaPlayer?.Position ?? 0) * (_mediaPlayer?.Length ?? 0);

    /// <summary>
    /// The MediaPlayer instance for binding to a VideoView.
    /// </summary>
    public MediaPlayer? Player => _mediaPlayer;

    /// <summary>
    /// Initialize with a shared LibVLC instance.
    /// This allows the video playback service to share the LibVLC core with
    /// the background service, reducing resource usage.
    /// </summary>
    /// <param name="libVLC">Shared LibVLC instance.</param>
    public void Initialize(LibVLC libVLC)
    {
        if (_isInitialized) return;

        _libVLC = libVLC;
        _mediaPlayer = new MediaPlayer(_libVLC);

        // Subscribe to end-reached event
        _mediaPlayer.EndReached += OnEndReached;
        _mediaPlayer.PositionChanged += OnPositionChanged;

        _isInitialized = true;
    }

    /// <summary>
    /// Play a video file (single playback, no loop).
    /// </summary>
    /// <param name="videoPath">Full path to the video file.</param>
    public void Play(string videoPath)
    {
        if (!_isInitialized || _libVLC == null || _mediaPlayer == null)
            return;

        if (string.IsNullOrEmpty(videoPath))
            return;

        Stop();

        var media = new Media(_libVLC, new Uri(videoPath));
        _mediaPlayer.Play(media);
        IsPaused = false;
    }

    /// <summary>
    /// Pause the current video playback.
    /// </summary>
    public void Pause()
    {
        if (_mediaPlayer == null || !_mediaPlayer.IsPlaying) return;

        _mediaPlayer.Pause();
        IsPaused = true;
    }

    /// <summary>
    /// Resume paused video playback.
    /// </summary>
    public void Resume()
    {
        if (_mediaPlayer == null || !IsPaused) return;

        _mediaPlayer.Play();
        IsPaused = false;
    }

    /// <summary>
    /// Stop video playback completely.
    /// </summary>
    public void Stop()
    {
        if (_mediaPlayer == null) return;

        if (_mediaPlayer.IsPlaying || IsPaused)
        {
            _mediaPlayer.Stop();
        }

        IsPaused = false;
    }

    /// <summary>
    /// Seek to a specific position in the video.
    /// </summary>
    /// <param name="positionMs">Target position in milliseconds.</param>
    public void Seek(double positionMs)
    {
        if (_mediaPlayer == null || _mediaPlayer.Length <= 0) return;

        var normalizedPosition = (float)(positionMs / _mediaPlayer.Length);
        normalizedPosition = Math.Clamp(normalizedPosition, 0f, 1f);
        _mediaPlayer.Position = normalizedPosition;
    }

    /// <summary>
    /// Get the total duration of the current media in milliseconds.
    /// </summary>
    public double GetDuration()
    {
        return _mediaPlayer?.Length ?? 0;
    }

    /// <summary>
    /// Get the current playback position in milliseconds.
    /// </summary>
    public double GetPosition()
    {
        if (_mediaPlayer == null) return 0;
        return _mediaPlayer.Position * _mediaPlayer.Length;
    }

    private void OnEndReached(object? sender, EventArgs e)
    {
        IsPaused = false;
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    private void OnPositionChanged(object? sender, MediaPlayerPositionChangedEventArgs e)
    {
        if (_mediaPlayer == null) return;
        var positionMs = e.Position * _mediaPlayer.Length;
        PositionChanged?.Invoke(this, positionMs);
    }

    /// <summary>
    /// Release resources.
    /// </summary>
    public void Dispose()
    {
        if (_mediaPlayer != null)
        {
            _mediaPlayer.EndReached -= OnEndReached;
            _mediaPlayer.PositionChanged -= OnPositionChanged;

            if (_mediaPlayer.IsPlaying)
                _mediaPlayer.Stop();

            _mediaPlayer.Dispose();
            _mediaPlayer = null;
        }

        // Note: We don't dispose _libVLC here since it's shared
        _libVLC = null;
        _isInitialized = false;
    }
}
