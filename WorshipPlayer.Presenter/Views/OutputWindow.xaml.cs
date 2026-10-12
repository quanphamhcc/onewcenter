using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Presenter.Services;

namespace WorshipPlayer.Presenter.Views;

/// <summary>
/// Full-screen output window for congregation display.
/// 
/// Uses a double-buffer rendering approach:
/// - SlideA and SlideB are overlapping grids
/// - Transitions animate opacity between them
/// - Content is loaded into the "incoming" buffer while the "current" is showing
/// - After transition completes, the buffers swap roles
/// 
/// Video support:
/// - Motion backgrounds: looping video behind lyrics (VideoBackgroundService)
/// - Full-screen video: Video service items played full screen (VideoPlaybackService)
/// </summary>
public partial class OutputWindow : Window
{
    private bool _isSlideAActive = true; // Which buffer is currently showing
    private SlideTheme _currentTheme = new();
    private bool _isBlacked = false;
    private bool _showChords = false;
    private readonly ChordProParserService _chordParser = new();

    // Live pitch-shift offset applied to shown chords (F5). 0 = no transposition.
    private int _pitchSemitones = 0;
    // The slide currently displayed, retained so SetPitchShift can re-render it.
    private Slide? _currentSlide;
    private SlideTransition _currentTransition = SlideTransition.Fade;
    private int _currentDurationMs = 500;

    // Video services
    private readonly VideoBackgroundService _videoBackgroundService = new();
    private readonly VideoPlaybackService _videoPlaybackService = new();
    private bool _isVideoMode = false;

    /// <summary>
    /// When true, a user close ([X]) is intercepted and the window is hidden instead of destroyed,
    /// preserving this single instance and its LibVLC services (BUG fix: closing while LIVE on a
    /// single monitor used to orphan the window and leak VLC decode memory). The host
    /// (PresenterIntegration / OperatorWindow) sets this true while the service is LIVE and false
    /// otherwise, so a genuine non-live close or app shutdown still destroys the window normally.
    /// </summary>
    public bool SuppressDestroyWhileLive { get; set; }

    /// <summary>
    /// Fired when full-screen video playback ends (for auto-advance to next service item).
    /// </summary>
    public event EventHandler? VideoPlaybackEnded;

    /// <summary>
    /// Whether to show inline chord notation on the output.
    /// When false (default for congregation), chords are stripped from displayed text.
    /// When true (stage mode), chords are shown inline.
    /// </summary>
    public bool ShowChords
    {
        get => _showChords;
        set => _showChords = value;
    }

    /// <summary>
    /// Whether the window is currently in full-screen video playback mode.
    /// </summary>
    public bool IsVideoMode => _isVideoMode;

    public OutputWindow()
    {
        InitializeComponent();
        InitializeVideoServices();
    }

    /// <summary>
    /// Initialize video services and wire up events.
    /// </summary>
    private void InitializeVideoServices()
    {
        _videoBackgroundService.Initialize();
        if (_videoBackgroundService.LibVLCInstance != null)
        {
            _videoPlaybackService.Initialize(_videoBackgroundService.LibVLCInstance);
        }

        _videoPlaybackService.PlaybackEnded += OnVideoPlaybackEnded;
    }

    /// <summary>
    /// Intercept a user close while the service is LIVE: cancel the close and hide the window
    /// instead of destroying it, so the single OutputWindow instance and its LibVLC services
    /// survive. GO LIVE then just re-shows the existing instance. A genuine (non-live) close or
    /// app shutdown leaves SuppressDestroyWhileLive false, so the window closes normally and
    /// OnClosed disposes everything.
    /// </summary>
    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (SuppressDestroyWhileLive)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnClosing(e);
    }

    /// <summary>
    /// Tear down everything this window owns on a real close / app exit: stop the alert timer,
    /// unsubscribe the playback-ended handler, and dispose both LibVLC-backed video services
    /// (Dispose clears the loop flag, unsubscribes EndReached, and releases the native decoder).
    /// This prevents the motion-background self-restart loop from leaking memory on an orphaned
    /// window.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        _alertTimer?.Stop();
        _videoPlaybackService.PlaybackEnded -= OnVideoPlaybackEnded;
        _videoBackgroundService.Dispose();
        _videoPlaybackService.Dispose();
    }

    /// <summary>
    /// Set chord display mode. When show is false, chords are stripped from text.
    /// Can be called from the operator window to toggle chord visibility.
    /// </summary>
    public void SetChordDisplay(bool show)
    {
        _showChords = show;
    }

    /// <summary>
    /// Place this window on a specific monitor and go full-screen.
    /// Only called when user has explicitly configured an output display.
    /// Switches to borderless for clean full-screen presentation.
    /// </summary>
    public void PlaceOnDisplay(DisplayInfo display)
    {
        // Switch to borderless for full-screen output
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Normal;
        Left = display.Left;
        Top = display.Top;
        Width = display.Width;
        Height = display.Height;
        WindowState = WindowState.Maximized;
        Topmost = true;
        ShowInTaskbar = false;
    }

    /// <summary>
    /// Go full-screen on the current monitor (used when going "Live" without a configured display).
    /// </summary>
    /// <param name="topmost">
    /// Whether the full-screen window stays on top. Pass false on a single-monitor rig so the
    /// operator can Alt-Tab back to the command center (F6).
    /// </param>
    public void GoFullScreen(bool topmost = true)
    {
        WindowStyle = WindowStyle.None;
        WindowState = WindowState.Maximized;
        Topmost = topmost;
    }

    /// <summary>
    /// Exit full-screen, return to windowed mode (used when going "Black" or closing).
    /// Restores title bar so user can close/move/resize.
    /// </summary>
    public void ExitFullScreen()
    {
        Topmost = false;
        WindowState = WindowState.Normal;
        WindowStyle = WindowStyle.SingleBorderWindow;
        Width = 960;
        Height = 540;
        ShowInTaskbar = true;
    }

    /// <summary>
    /// Apply a theme to the output window.
    /// </summary>
    public void ApplyTheme(SlideTheme theme)
    {
        _currentTheme = theme;

        // Apply background
        switch (theme.BackgroundType)
        {
            case BackgroundType.SolidColor:
                BackgroundImage.Visibility = Visibility.Collapsed;
                StopMotionBackground();
                RootGrid.Background = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString(theme.BackgroundColor));
                break;

            case BackgroundType.Image:
                StopMotionBackground();
                var backgroundPath = ResolveImagePath(theme.BackgroundImage);
                if (!string.IsNullOrEmpty(backgroundPath) && System.IO.File.Exists(backgroundPath))
                {
                    try
                    {
                        BackgroundImage.Source = new BitmapImage(new Uri(backgroundPath));
                        BackgroundImage.Opacity = theme.BackgroundImageOpacity;
                        BackgroundImage.Visibility = Visibility.Visible;
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[OutputWindow] Background image load failed: {ex.Message}");
                        BackgroundImage.Visibility = Visibility.Collapsed;
                    }
                }
                break;

            case BackgroundType.Video:
                BackgroundImage.Visibility = Visibility.Collapsed;
                if (!string.IsNullOrEmpty(theme.MotionBackgroundPath))
                {
                    StartMotionBackground(theme.MotionBackgroundPath, theme.MotionBackgroundOpacity);
                }
                break;
        }

        // Gradient overlay
        GradientOverlay.Visibility = theme.UseGradientOverlay
            ? Visibility.Visible
            : Visibility.Collapsed;

        // Logo
        var logoPath = ResolveImagePath(theme.LogoPath);
        bool logoShown = false;
        if (!string.IsNullOrEmpty(logoPath) && System.IO.File.Exists(logoPath))
        {
            try
            {
                LogoWatermark.Source = new BitmapImage(new Uri(logoPath));
                LogoWatermark.Opacity = theme.LogoOpacity;
                LogoWatermark.Visibility = Visibility.Visible;

                // Position logo
                LogoWatermark.HorizontalAlignment = theme.LogoPosition switch
                {
                    LogoPosition.TopLeft or LogoPosition.BottomLeft => HorizontalAlignment.Left,
                    LogoPosition.TopRight or LogoPosition.BottomRight => HorizontalAlignment.Right,
                    _ => HorizontalAlignment.Center
                };
                LogoWatermark.VerticalAlignment = theme.LogoPosition switch
                {
                    LogoPosition.TopLeft or LogoPosition.TopRight => System.Windows.VerticalAlignment.Top,
                    LogoPosition.BottomLeft or LogoPosition.BottomRight => System.Windows.VerticalAlignment.Bottom,
                    _ => System.Windows.VerticalAlignment.Center
                };
                logoShown = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OutputWindow] Logo image load failed: {ex.Message}");
            }
        }
        if (!logoShown)
        {
            LogoWatermark.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Display a new slide with the specified transition.
    /// </summary>
    public void ShowSlide(Slide slide, SlideTransition transition, int durationMs)
    {
        if (_isBlacked || _isVideoMode) return;

        // Remember the active slide so a later pitch change can re-render it (F5).
        _currentSlide = slide;
        _currentTransition = transition;
        _currentDurationMs = durationMs;

        // Load content into the INACTIVE buffer
        if (_isSlideAActive)
        {
            LoadSlideIntoBuffer(slide, SlideBText, SlideBHeader, SlideBFooter);
            TransitionToB(transition, durationMs);
        }
        else
        {
            LoadSlideIntoBuffer(slide, SlideAText, SlideAHeader, SlideAFooter);
            TransitionToA(transition, durationMs);
        }

        _isSlideAActive = !_isSlideAActive;
    }

    /// <summary>
    /// Set the live pitch-shift offset (semitones) and re-render the current slide so shown
    /// chords reflect the new key (F5). +N transposes chords up by N; 0 shows the text unchanged.
    /// When chords are hidden the pitch shift has no visible effect.
    /// </summary>
    public void SetPitchShift(int semitones)
    {
        _pitchSemitones = semitones;

        // Re-render the active slide through the same buffer that is currently showing so the
        // operator sees the transposed chords without a slide advance.
        if (_currentSlide != null && !_isBlacked && !_isVideoMode)
        {
            if (_isSlideAActive)
                LoadSlideIntoBuffer(_currentSlide, SlideAText, SlideAHeader, SlideAFooter);
            else
                LoadSlideIntoBuffer(_currentSlide, SlideBText, SlideBHeader, SlideBFooter);
        }
    }

    /// <summary>
    /// Switch to full-screen video playback mode.
    /// Hides lyrics layers and shows the video at full screen.
    /// Used for Video service items.
    /// </summary>
    /// <param name="videoPath">Full path to the video file to play.</param>
    public void ShowVideo(string videoPath)
    {
        if (string.IsNullOrEmpty(videoPath)) return;

        _isVideoMode = true;

        // Hide lyric layers
        SlideA.Visibility = Visibility.Collapsed;
        SlideB.Visibility = Visibility.Collapsed;
        LogoWatermark.Visibility = Visibility.Collapsed;

        // Show and bind full-screen video view
        FullScreenVideoView.Visibility = Visibility.Visible;
        if (_videoPlaybackService.Player != null)
        {
            // VLC not in XAML — video assigned programmatically when available
        }

        // Start playback
        _videoPlaybackService.Play(videoPath);
    }

    /// <summary>
    /// Stop full-screen video playback and return to normal lyric mode.
    /// </summary>
    public void StopVideo()
    {
        _videoPlaybackService.Stop();
        _isVideoMode = false;

        // Restore lyric layers
        FullScreenVideoView.Visibility = Visibility.Collapsed;
        SlideA.Visibility = Visibility.Visible;
        SlideB.Visibility = Visibility.Visible;

        // Restore logo if configured
        if (!string.IsNullOrEmpty(_currentTheme.LogoPath) && System.IO.File.Exists(_currentTheme.LogoPath))
        {
            LogoWatermark.Visibility = Visibility.Visible;
        }
    }

    /// <summary>
    /// Show a blank/black screen.
    /// </summary>
    public void ShowBlack()
    {
        _isBlacked = true;
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        SlideA.BeginAnimation(OpacityProperty, fadeOut);
        SlideB.BeginAnimation(OpacityProperty, fadeOut);
        BackgroundLayer.BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Show the logo/watermark on black.
    /// </summary>
    public void ShowLogo()
    {
        _isBlacked = true;
        var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(300));
        SlideA.BeginAnimation(OpacityProperty, fadeOut);
        SlideB.BeginAnimation(OpacityProperty, fadeOut);

        // Keep logo visible
        LogoWatermark.Opacity = 1.0;
        LogoWatermark.HorizontalAlignment = HorizontalAlignment.Center;
        LogoWatermark.VerticalAlignment = System.Windows.VerticalAlignment.Center;
    }

    /// <summary>
    /// Resume showing slides (unblack).
    /// </summary>
    public void Unblack()
    {
        _isBlacked = false;
        var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(300));
        BackgroundLayer.BeginAnimation(OpacityProperty, fadeIn);

        if (_isSlideAActive)
            SlideA.BeginAnimation(OpacityProperty, fadeIn);
        else
            SlideB.BeginAnimation(OpacityProperty, fadeIn);

        // Restore logo opacity
        if (!string.IsNullOrEmpty(_currentTheme.LogoPath))
            LogoWatermark.Opacity = _currentTheme.LogoOpacity;
    }

    // ─── Alert and Countdown Overlay Methods ──────────────────────

    private DispatcherTimer? _alertTimer;

    /// <summary>
    /// Show an alert overlay on the output window. Auto-hides after the specified duration.
    /// </summary>
    /// <param name="template">Alert template with text and styling.</param>
    /// <param name="durationMs">Duration in milliseconds before auto-hide. 0 = no auto-hide.</param>
    public void ShowAlert(AlertTemplate template, int durationMs)
    {
        AlertOverlay.Text = template.Text;
        AlertOverlay.FontSize = template.FontSize;
        AlertOverlay.FontFamily = new FontFamily(template.FontFamily);

        try
        {
            AlertOverlay.Foreground = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(template.FontColor));
        }
        catch
        {
            AlertOverlay.Foreground = new SolidColorBrush(Colors.White);
        }

        AlertOverlay.Visibility = Visibility.Visible;

        // Auto-hide after duration
        _alertTimer?.Stop();
        if (durationMs > 0)
        {
            _alertTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(durationMs)
            };
            _alertTimer.Tick += (s, e) =>
            {
                _alertTimer?.Stop();
                HideAlert();
            };
            _alertTimer.Start();
        }
    }

    /// <summary>
    /// Hide the alert overlay.
    /// </summary>
    public void HideAlert()
    {
        _alertTimer?.Stop();
        AlertOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Show a countdown time string on the output window as an overlay.
    /// </summary>
    /// <param name="timeText">Formatted time text (e.g., "05:30" or "01:23:45").</param>
    public void ShowCountdown(string timeText)
    {
        CountdownOverlay.Text = timeText;
        CountdownOverlay.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Hide the countdown overlay.
    /// </summary>
    public void HideCountdown()
    {
        CountdownOverlay.Visibility = Visibility.Collapsed;
    }

    // ─── Video Private Methods ────────────────────────────────────

    /// <summary>
    /// Start a motion background video (looping behind lyrics).
    /// </summary>
    private void StartMotionBackground(string videoPath, double opacity)
    {
        _videoBackgroundService.SetOpacity(opacity);
        // BackgroundVideoView.Opacity = opacity; // VLC not in XAML
        BackgroundVideoView.Visibility = Visibility.Visible;

        if (_videoBackgroundService.Player != null)
        {
            // VLC not in XAML — video assigned programmatically when available
        }

        _videoBackgroundService.PlayBackground(videoPath, loop: true);
    }

    /// <summary>
    /// Stop the motion background video.
    /// </summary>
    private void StopMotionBackground()
    {
        _videoBackgroundService.StopBackground();
        BackgroundVideoView.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Handle video playback ended event - fire VideoPlaybackEnded for auto-advance.
    /// </summary>
    private void OnVideoPlaybackEnded(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            StopVideo();
            VideoPlaybackEnded?.Invoke(this, EventArgs.Empty);
        });
    }

    // ─── Private Methods ──────────────────────────────────────────

    private void LoadSlideIntoBuffer(Slide slide, System.Windows.Controls.TextBlock textBlock,
        System.Windows.Controls.TextBlock headerBlock, System.Windows.Controls.TextBlock footerBlock)
    {
        // Apply chord display toggle: strip chords for congregation, keep (and transpose by the
        // live pitch offset) for stage. Mirrors PresenterEngine.GetStageText — +N up, 0 unchanged.
        var slideText = slide.Content?.Text ?? "";
        string displayText;
        if (_showChords)
        {
            displayText = _pitchSemitones == 0
                ? slideText
                : _chordParser.Transpose(slideText, _pitchSemitones);
        }
        else
        {
            displayText = _chordParser.StripChords(slideText);
        }
        textBlock.Text = displayText;
        headerBlock.Text = slide.Content?.Header ?? "";
        footerBlock.Text = slide.Content?.Footer ?? "";

        // Apply theme fonts
        textBlock.FontFamily = new FontFamily(_currentTheme.PrimaryFontFamily);
        textBlock.FontSize = _currentTheme.PrimaryFontSize;
        textBlock.FontWeight = _currentTheme.PrimaryFontBold ? FontWeights.Bold : FontWeights.Normal;
        textBlock.FontStyle = _currentTheme.PrimaryFontItalic ? FontStyles.Italic : FontStyles.Normal;
        textBlock.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(_currentTheme.PrimaryFontColor));

        textBlock.TextAlignment = _currentTheme.PrimaryAlignment switch
        {
            Core.Models.TextAlignment.Left => System.Windows.TextAlignment.Left,
            Core.Models.TextAlignment.Right => System.Windows.TextAlignment.Right,
            _ => System.Windows.TextAlignment.Center
        };

        // Secondary (header)
        headerBlock.FontFamily = new FontFamily(_currentTheme.SecondaryFontFamily);
        headerBlock.FontSize = _currentTheme.SecondaryFontSize;
        headerBlock.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(_currentTheme.SecondaryFontColor));

        // Footer
        footerBlock.FontSize = _currentTheme.FooterFontSize;
        footerBlock.Foreground = new SolidColorBrush(
            (Color)ColorConverter.ConvertFromString(_currentTheme.FooterFontColor));

        // Foreground image (QR code etc.) — resolve relative paths against the app base dir and
        // never let a missing/corrupt image throw during render (F9/F10).
        var foregroundPath = ResolveImagePath(slide.Content?.ForegroundImage);
        bool foregroundShown = false;
        if (!string.IsNullOrEmpty(foregroundPath) && System.IO.File.Exists(foregroundPath))
        {
            try
            {
                ForegroundImage.Source = new BitmapImage(new Uri(foregroundPath));
                ForegroundImage.Opacity = 1;
                foregroundShown = true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[OutputWindow] Foreground image load failed: {ex.Message}");
            }
        }
        if (!foregroundShown)
        {
            ForegroundImage.Opacity = 0;
        }
    }

    /// <summary>
    /// Resolve a possibly-relative image path against <see cref="AppDomain.CurrentDomain.BaseDirectory"/>
    /// so relative slide/theme image paths load regardless of the working directory (F9/F10).
    /// Absolute paths are returned unchanged; null/empty returns null.
    /// </summary>
    private static string? ResolveImagePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (System.IO.Path.IsPathRooted(path)) return path;
        return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
    }

    private void TransitionToA(SlideTransition transition, int durationMs)
    {
        switch (transition)
        {
            case SlideTransition.Cut:
                SlideA.Opacity = 1;
                SlideB.Opacity = 0;
                break;

            case SlideTransition.Fade:
            case SlideTransition.Dissolve:
                var fadeInA = new DoubleAnimation(1, TimeSpan.FromMilliseconds(durationMs));
                var fadeOutB = new DoubleAnimation(0, TimeSpan.FromMilliseconds(durationMs));
                SlideA.BeginAnimation(OpacityProperty, fadeInA);
                SlideB.BeginAnimation(OpacityProperty, fadeOutB);
                break;

            case SlideTransition.SlideLeft:
            case SlideTransition.SlideUp:
                // For now, treat as fade
                var slideInA = new DoubleAnimation(1, TimeSpan.FromMilliseconds(durationMs));
                var slideOutB = new DoubleAnimation(0, TimeSpan.FromMilliseconds(durationMs));
                SlideA.BeginAnimation(OpacityProperty, slideInA);
                SlideB.BeginAnimation(OpacityProperty, slideOutB);
                break;
        }
    }

    private void TransitionToB(SlideTransition transition, int durationMs)
    {
        switch (transition)
        {
            case SlideTransition.Cut:
                SlideB.Opacity = 1;
                SlideA.Opacity = 0;
                break;

            case SlideTransition.Fade:
            case SlideTransition.Dissolve:
                var fadeInB = new DoubleAnimation(1, TimeSpan.FromMilliseconds(durationMs));
                var fadeOutA = new DoubleAnimation(0, TimeSpan.FromMilliseconds(durationMs));
                SlideB.BeginAnimation(OpacityProperty, fadeInB);
                SlideA.BeginAnimation(OpacityProperty, fadeOutA);
                break;

            case SlideTransition.SlideLeft:
            case SlideTransition.SlideUp:
                var slideInB = new DoubleAnimation(1, TimeSpan.FromMilliseconds(durationMs));
                var slideOutA = new DoubleAnimation(0, TimeSpan.FromMilliseconds(durationMs));
                SlideB.BeginAnimation(OpacityProperty, slideInB);
                SlideA.BeginAnimation(OpacityProperty, slideOutA);
                break;
        }
    }
}
