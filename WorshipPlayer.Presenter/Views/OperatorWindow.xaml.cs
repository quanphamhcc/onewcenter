using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Core.Services.Bible;
using WorshipPlayer.Core.Services.Interfaces;
using WorshipPlayer.Presenter.Services;
using WorshipPlayer.Presenter.Views.SongLibrary;
using WorshipPlayer.Presenter.Views.Scripture;
using WorshipPlayer.Presenter.Views.MediaLibrary;
using WorshipPlayer.Presenter.Views.Tools;
using WorshipPlayer.Presenter.Views.Settings;
using WorshipPlayer.Presenter.Views.ServiceBuilder;

namespace WorshipPlayer.Presenter.Views;

/// <summary>
/// OWC Presenter - Operator Command Center
/// 
/// Redesigned operator window with the OWC Design Identity:
/// - Live preview hero boxes at top (Audience + Stage)
/// - Header bar with OWC logo, search, settings
/// - Collapsible service order sidebar (left)
/// - Slide grid with zoom control (center)
/// - Full-width transport bar (bottom)
/// - Keyboard shortcuts for hands-free operation
/// </summary>
public partial class OperatorWindow : Window
{
    private PresenterEngine? _engine;
    private OutputWindow? _outputWindow;
    private ThemeManager? _themeManager;
    private NdiOutputService? _ndiOutputService;
    private OutputCaptureService? _outputCaptureService;
    private DisplayManager? _displayManager;
    private Action<PresenterSettings>? _onSaveSettings;

    // F6: single-monitor Go-Live hooks, supplied by the host (PresenterIntegration). The callback
    // lazily creates/full-screens the output window on the sole monitor; the predicate reports
    // whether the current output is a single-monitor full-screen (so Esc reclaims the screen).
    private Action? _onGoLive;
    private Func<bool>? _isSingleMonitorOutput;

    /// <summary>
    /// Register the single-monitor Go-Live callback and the single-monitor-output predicate (F6).
    /// </summary>
    public void SetGoLiveHandlers(Action? onGoLive, Func<bool>? isSingleMonitorOutput)
    {
        _onGoLive = onGoLive;
        _isSingleMonitorOutput = isSingleMonitorOutput;
    }

    /// <summary>
    /// Load a service order file via the shared ServiceOrderManager (F1). Null/exception degrade
    /// to a non-fatal message box; never crashes. Separated out so LoadService_Click can await it.
    /// </summary>
    private async Task LoadServiceOrderAsync(string path)
    {
        if (_serviceOrderManager == null) return;
        try
        {
            var loaded = await _serviceOrderManager.LoadAsync(path);
            if (loaded == null)
            {
                MessageBox.Show(this, "The selected service order could not be loaded (missing or invalid file).",
                    "Load Service", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not load the service order:\n{ex.Message}",
                "Load Service", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
    private bool _ndiActive;
    private bool _isEditMode;
    private bool _isSidebarCollapsed;
    private int _selectedSlideIndex = -1;
    private int _lastActiveSlideIndex = -1;

    // Services for panels and tools
    private ISongLibraryService? _songLibraryService;
    private IMediaLibraryService? _mediaLibraryService;
    private ISearchService? _searchService;
    private IExportService? _exportService;
    private IAlertService? _alertService;
    private ICountdownService? _countdownService;
    private SongUsageService? _songUsageService;
    private IBibleProvider? _bibleProvider;
    private ServiceOrderManager? _serviceOrderManager;
    private SongManager? _songManager;

    // Navigation tab state
    private string _activeTab = "Services";
    private SongLibraryPanel? _songLibraryPanel;
    private ScriptureBrowserPanel? _scriptureBrowserPanel;
    private MediaLibraryPanel? _mediaLibraryPanel;

    // OWC Design colors
    private static readonly Color AccentCyan = (Color)ColorConverter.ConvertFromString("#00D4FF");
    private static readonly Color LivePink = (Color)ColorConverter.ConvertFromString("#FF3366");
    private static readonly Color ConnectedGreen = (Color)ColorConverter.ConvertFromString("#00E676");
    private static readonly Color WarningOrange = (Color)ColorConverter.ConvertFromString("#FFAB00");
    private static readonly Color PanelBg = (Color)ColorConverter.ConvertFromString("#1A1D27");
    private static readonly Color SurfaceBg = (Color)ColorConverter.ConvertFromString("#252A36");
    private static readonly Color TextPrimary = (Color)ColorConverter.ConvertFromString("#F0F2F5");
    private static readonly Color TextSecondary = (Color)ColorConverter.ConvertFromString("#8B92A5");
    private static readonly Color TextMuted = (Color)ColorConverter.ConvertFromString("#4A5068");
    private static readonly Color BorderColor = (Color)ColorConverter.ConvertFromString("#2A2E3A");

    public OperatorWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Clean up engine and theme event subscriptions to prevent memory leaks.
    /// </summary>
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        if (_engine != null)
        {
            _engine.SlideChanged -= OnSlideChanged;
            _engine.ItemChanged -= OnItemChanged;
            _engine.DisplayStateChanged -= OnDisplayStateChanged;
            _engine.PitchShiftChanged -= OnPitchShiftChanged;
            _engine.TempoChanged -= OnTempoChanged;
            _engine.SongLoaded -= OnSongLoaded;
        }

        if (_themeManager != null)
        {
            _themeManager.ThemeChanged -= OnThemeChanged;
        }

        if (_ndiOutputService != null)
        {
            _ndiOutputService.StateChanged -= OnNdiStateChanged;
        }

        if (_outputCaptureService != null)
        {
            // Stop the capture timer (up to 60 FPS RenderTargetBitmap allocations) before
            // unsubscribing so NDI capture cannot keep running against an orphaned output.
            _outputCaptureService.Stop();
            _outputCaptureService.FrameCaptured -= OnFrameCaptured;
        }

        if (_alertService != null)
        {
            _alertService.AlertShown -= OnAlertShown;
            _alertService.AlertDismissed -= OnAlertDismissed;
        }

        if (_countdownService != null)
        {
            _countdownService.Tick -= OnCountdownTick;
            _countdownService.Completed -= OnCountdownCompleted;
        }
    }

    /// <summary>
    /// Initialize with the presenter engine and output window.
    /// </summary>
    public void Initialize(PresenterEngine engine, OutputWindow? outputWindow)
    {
        Initialize(engine, outputWindow, null);
    }

    /// <summary>
    /// Initialize with the presenter engine, output window, and theme manager.
    /// </summary>
    public void Initialize(PresenterEngine engine, OutputWindow? outputWindow, ThemeManager? themeManager)
    {
        Initialize(engine, outputWindow, themeManager, null, null);
    }

    /// <summary>
    /// Full initialization with display manager and settings save callback.
    /// </summary>
    public void Initialize(PresenterEngine engine, OutputWindow? outputWindow, ThemeManager? themeManager,
        DisplayManager? displayManager, Action<PresenterSettings>? onSaveSettings)
    {
        _engine = engine;
        _outputWindow = outputWindow;
        _themeManager = themeManager;
        _displayManager = displayManager;
        _onSaveSettings = onSaveSettings;

        // Subscribe to engine events
        _engine.SlideChanged += OnSlideChanged;
        _engine.ItemChanged += OnItemChanged;
        _engine.DisplayStateChanged += OnDisplayStateChanged;
        _engine.PitchShiftChanged += OnPitchShiftChanged;
        _engine.TempoChanged += OnTempoChanged;
        _engine.SongLoaded += OnSongLoaded;

        // Subscribe to theme manager events
        if (_themeManager != null)
        {
            _themeManager.ThemeChanged += OnThemeChanged;
            PopulateThemeSelector();
        }

        // Load initial state
        RefreshServiceOrderList();
        UpdatePreviews();
        RefreshSlideThumbnails();
    }

    /// <summary>
    /// Update the OutputWindow reference (called when Display Settings creates/moves the output window).
    /// Ensures slides are forwarded to the correct output.
    /// </summary>
    public void SetOutputWindow(OutputWindow? outputWindow)
    {
        _outputWindow = outputWindow;
    }

    /// <summary>
    /// Set all services for panels and tools.
    /// Called by PresenterIntegration after Initialize().
    /// </summary>
    public void SetServices(
        ISongLibraryService? songLibraryService,
        IMediaLibraryService? mediaLibraryService,
        ISearchService? searchService,
        IExportService? exportService,
        IAlertService? alertService,
        ICountdownService? countdownService,
        SongUsageService? songUsageService,
        IBibleProvider? bibleProvider,
        ServiceOrderManager? serviceOrderManager,
        SongManager? songManager)
    {
        _songLibraryService = songLibraryService;
        _mediaLibraryService = mediaLibraryService;
        _searchService = searchService;
        _exportService = exportService;
        _alertService = alertService;
        _countdownService = countdownService;
        _songUsageService = songUsageService;
        _bibleProvider = bibleProvider;
        _serviceOrderManager = serviceOrderManager;
        _songManager = songManager;

        // Subscribe to alert/countdown events for output window
        if (_alertService != null)
        {
            _alertService.AlertShown += OnAlertShown;
            _alertService.AlertDismissed += OnAlertDismissed;
        }

        if (_countdownService != null)
        {
            _countdownService.Tick += OnCountdownTick;
            _countdownService.Completed += OnCountdownCompleted;
        }
    }

    private void OnAlertShown(object? sender, AlertTemplate template)
    {
        Dispatcher.Invoke(() =>
        {
            _outputWindow?.ShowAlert(template, 5000);
        });
    }

    private void OnAlertDismissed(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _outputWindow?.HideAlert();
        });
    }

    private void OnCountdownTick(object? sender, long remainingMs)
    {
        Dispatcher.Invoke(() =>
        {
            var ts = TimeSpan.FromMilliseconds(remainingMs);
            var timeText = ts.TotalHours >= 1
                ? $"{(int)ts.TotalHours:D2}:{ts.Minutes:D2}:{ts.Seconds:D2}"
                : $"{ts.Minutes:D2}:{ts.Seconds:D2}";
            _outputWindow?.ShowCountdown(timeText);
        });
    }

    private void OnCountdownCompleted(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _outputWindow?.HideCountdown();
        });
    }

    // --- Engine Event Handlers ---

    private void OnSlideChanged(object? sender, SlideChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            UpdatePreviews();
            UpdateSlideCounter();
            UpdateSlideThumbnailHighlights();

            // Forward to output window
            if (e.NewSlide != null && _outputWindow != null)
            {
                _outputWindow.ShowSlide(e.NewSlide, e.Transition, e.TransitionDurationMs);
            }
        });
    }

    private void OnItemChanged(object? sender, ServiceItemChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            CurrentItemTitle.Text = e.NewItem?.Title ?? "";
            CurrentItemSubtitle.Text = e.NewItem?.Type.ToString() ?? "";
            RefreshSlideThumbnails();
            UpdatePreviews();
            UpdateSlideCounter();

            // Highlight in service order list (ItemsControl doesn't support selection)
            // Future: add click handlers on items for navigation
        });
    }

    private void OnDisplayStateChanged(object? sender, DisplayStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (e.IsBlacked)
            {
                LiveButton.Content = "GO LIVE";
                LiveButton.Background = new SolidColorBrush(LivePink);
                LiveBadge.Visibility = Visibility.Collapsed;
                AudiencePreviewBorder.BorderBrush = new SolidColorBrush(BorderColor);
                _outputWindow?.ShowBlack();
            }
            else if (e.IsLive)
            {
                LiveButton.Content = "LIVE";
                LiveButton.Background = new SolidColorBrush(LivePink);
                LiveBadge.Visibility = Visibility.Visible;
                AudiencePreviewBorder.BorderBrush = new SolidColorBrush(LivePink);
            }
        });
    }

    private void OnPitchShiftChanged(object? sender, PitchShiftChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            PitchInfoText.Text = e.Semitones == 0 ? "0" : $"{e.Semitones:+#;-#;0}";

            // F5: push the new pitch offset to the congregation output and re-render the current
            // slide so shown chords transpose live.
            _outputWindow?.SetPitchShift(e.Semitones);
            var current = _engine?.CurrentSlide;
            if (current != null)
            {
                _outputWindow?.ShowSlide(current, SlideTransition.Cut, 0);
            }
        });
    }

    private void OnTempoChanged(object? sender, TempoChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            TempoInfoText.Text = $"{e.TempoPercent:F0}%";
        });
    }

    private void OnSongLoaded(object? sender, SongLoadedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            KeyInfoText.Text = !string.IsNullOrEmpty(e.Song.OriginalKey) ? e.Song.OriginalKey : "-";
            CapoInfoText.Text = "-";
        });
    }

    private void OnThemeChanged(object? sender, SlideTheme theme)
    {
        Dispatcher.Invoke(() =>
        {
            _outputWindow?.ApplyTheme(theme);
        });
    }

    // --- Button Click Handlers ---

    private void NextSlide_Click(object sender, RoutedEventArgs e) => _engine?.NextSlideAction();
    private void PrevSlide_Click(object sender, RoutedEventArgs e) => _engine?.PreviousSlideAction();
    private void NextItem_Click(object sender, RoutedEventArgs e) => _engine?.NextItemAction();
    private void PrevItem_Click(object sender, RoutedEventArgs e) => _engine?.PreviousItemAction();

    private void GoLive_Click(object sender, RoutedEventArgs e)
    {
        if (_engine == null) return;

        // Always (re)create/re-show the single-monitor output first. The hook is a no-op on a
        // dual-monitor rig and when the window already exists & is loaded; it recreates/reshows the
        // window and refreshes this window's _outputWindow reference via SetOutputWindow. Running it
        // unconditionally is what fixes "GO LIVE does nothing after the output was closed while LIVE"
        // (IsLive stays true, so the old IsBlacked||!IsLive guard never reached the hook).
        _onGoLive?.Invoke();

        if (_engine.IsBlacked || !_engine.IsLive)
        {
            _engine.GoLive();
        }
        _outputWindow?.Unblack();
    }

    private void GoBlack_Click(object sender, RoutedEventArgs e)
    {
        _engine?.GoBlack();
    }

    private void ShowLogo_Click(object sender, RoutedEventArgs e)
    {
        _engine?.ShowLogo();
        _outputWindow?.ShowLogo();
    }

    private void ToggleChords_Click(object sender, RoutedEventArgs e)
    {
        if (_engine == null) return;

        var settings = _engine.Settings;
        settings.CongregationShowChords = !settings.CongregationShowChords;

        // Apply to output window if it exists
        _outputWindow?.SetChordDisplay(settings.CongregationShowChords);

        if (settings.CongregationShowChords)
        {
            ChordsToggleBtn.Content = "CHORDS: ON";
            ChordsToggleBtn.Foreground = new SolidColorBrush(ConnectedGreen);
            ChordsToggleBtn.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#1A2E1A"));
        }
        else
        {
            ChordsToggleBtn.Content = "CHORDS: OFF";
            ChordsToggleBtn.Foreground = new SolidColorBrush(TextSecondary);
            ChordsToggleBtn.Background = new SolidColorBrush(SurfaceBg);
        }

        // Re-render output if window exists
        var currentSlide = _engine.CurrentSlide;
        if (currentSlide != null && _outputWindow != null)
        {
            _outputWindow.ShowSlide(currentSlide, SlideTransition.Cut, 0);
        }

        // Update the audience preview to reflect the chord toggle immediately
        UpdatePreviews();
    }

    private async void LoadService_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Service Order (*.json)|*.json",
            Title = "Load Service Order"
        };

        if (dialog.ShowDialog() == true)
        {
            // F1: actually load the chosen file. ServiceOrderLoaded wiring (PresenterIntegration)
            // sets the first item current and refreshes the operator list.
            await LoadServiceOrderAsync(dialog.FileName);
        }
    }

    private void NewService_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new AddServiceItemDialog();
            dialog.Owner = this;
            if (_serviceOrderManager != null)
                dialog.ServiceOrderManager = _serviceOrderManager;
            dialog.ItemAdded += (s, item) =>
            {
                RefreshServiceOrderList();
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowToolError("Add Service Item", ex.Message);
        }
    }

    private void ThemeSettings_Click(object sender, RoutedEventArgs e)
    {
        // Placeholder for theme editor
    }

    private void DisplaySettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.Owner = this;
            settingsWindow.PresenterSettings = _engine?.Settings;
            settingsWindow.OnSaveRequested = () =>
            {
                // Save presenter settings to disk and apply display changes
                if (_engine != null)
                    _onSaveSettings?.Invoke(_engine.Settings);
            };
            settingsWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowToolError("Settings", ex.Message);
        }
    }

    private void NdiToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_ndiActive)
        {
            StopNdiOutput();
        }
        else
        {
            StartNdiOutput();
        }
    }

    private void StartNdiOutput()
    {
        if (_outputWindow == null) return;

        if (_ndiOutputService == null)
        {
            var settings = _engine?.Settings ?? new PresenterSettings();
            _ndiOutputService = new NdiOutputService(settings);
            _ndiOutputService.SetNdiOutput(new StubNdiOutput());
            _ndiOutputService.StateChanged += OnNdiStateChanged;
        }

        if (_outputCaptureService == null)
        {
            _outputCaptureService = new OutputCaptureService();
            _outputCaptureService.FrameCaptured += OnFrameCaptured;
        }

        var presenterSettings = _engine?.Settings ?? new PresenterSettings();

        _ndiOutputService.Initialize();
        _ndiOutputService.Start();

        var target = _outputWindow.Content as FrameworkElement;
        if (target != null)
        {
            _outputCaptureService.Start(
                target,
                presenterSettings.NdiFps,
                presenterSettings.NdiResolutionWidth,
                presenterSettings.NdiResolutionHeight);
        }

        _ndiActive = true;
        UpdateNdiUi(true);
    }

    private void StopNdiOutput()
    {
        _outputCaptureService?.Stop();
        _ndiOutputService?.Stop();

        _ndiActive = false;
        UpdateNdiUi(false);
    }

    private void OnFrameCaptured(object? sender, FrameCapturedEventArgs e)
    {
        _ndiOutputService?.SendFrame(e.BgraPixels, e.Width, e.Height);
    }

    private void OnNdiStateChanged(object? sender, NdiStateChangedEventArgs e)
    {
        Dispatcher.Invoke(() => UpdateNdiUi(e.IsActive));
    }

    private void UpdateNdiUi(bool active)
    {
        NdiToggleButton.Content = active ? "ON" : "OFF";
        NdiToggleButton.Background = active
            ? new SolidColorBrush(ConnectedGreen)
            : new SolidColorBrush(SurfaceBg);

        NdiStatusDot.Fill = active
            ? new SolidColorBrush(ConnectedGreen)
            : new SolidColorBrush(TextMuted);

        NdiToggleStatusText.Text = active ? "Streaming" : "Inactive";
        NdiStatusText.Text = active ? "NDI ON" : "NDI OFF";
        NdiStatusText.Foreground = active
            ? new SolidColorBrush(ConnectedGreen)
            : new SolidColorBrush(TextSecondary);
    }

    private void ChordToggle_Changed(object sender, RoutedEventArgs e)
    {
        var isChecked = ChordToggleCheckBox.IsChecked == true;
        _outputWindow?.SetChordDisplay(isChecked);
    }

    private void ThemeSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_themeManager == null || ThemeSelector.SelectedIndex < 0) return;

        var themes = _themeManager.AvailableThemes;
        if (ThemeSelector.SelectedIndex < themes.Count)
        {
            _themeManager.SetCurrentTheme(themes[ThemeSelector.SelectedIndex].Id);
        }
    }

    // Service order item click handled via ItemsControl DataTemplate in future

    // --- Sidebar Collapse ---

    private void SidebarCollapse_Click(object sender, RoutedEventArgs e)
    {
        _isSidebarCollapsed = !_isSidebarCollapsed;

        if (_isSidebarCollapsed)
        {
            SidebarColumn.Width = new GridLength(48);
            SidebarCollapseBtn.Content = "\u25B6"; // right arrow
        }
        else
        {
            SidebarColumn.Width = new GridLength(220);
            SidebarCollapseBtn.Content = "\u25C0"; // left arrow
        }
    }

    // --- Edit Mode Toggle ---

    private void EditModeToggle_Click(object sender, RoutedEventArgs e)
    {
        _isEditMode = !_isEditMode;

        if (_isEditMode)
        {
            EditModeToggleBtn.Content = "EDIT MODE: ON";
            EditModeToggleBtn.Foreground = new SolidColorBrush(AccentCyan);
            EditModeToggleBtn.Background = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString("#1A2A36"));
        }
        else
        {
            EditModeToggleBtn.Content = "EDIT MODE: OFF";
            EditModeToggleBtn.Foreground = new SolidColorBrush(TextSecondary);
            EditModeToggleBtn.Background = new SolidColorBrush(SurfaceBg);
        }
    }

    // --- Live Preview Hero Click Handlers ---

    private void AudiencePreview_Click(object sender, MouseButtonEventArgs e)
    {
        // Placeholder: pop-out audience preview window
    }

    private void StagePreview_Click(object sender, MouseButtonEventArgs e)
    {
        // Placeholder: pop-out stage preview window
    }

    // --- Zoom Slider ---

    private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        // Re-render thumbnails at the new zoom size
        if (_engine != null)
        {
            RefreshSlideThumbnails();
        }
    }

    // --- Keyboard Shortcuts ---

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Don't intercept if a text-editable input has focus
        if (e.OriginalSource is TextBoxBase || e.OriginalSource is ComboBox) return;

        switch (e.Key)
        {
            case Key.Right:
            case Key.Space:
            case Key.PageDown:
                _engine?.NextSlideAction();
                e.Handled = true;
                break;

            case Key.Left:
            case Key.PageUp:
                _engine?.PreviousSlideAction();
                e.Handled = true;
                break;

            case Key.Down:
                _engine?.NextItemAction();
                e.Handled = true;
                break;

            case Key.Up:
                _engine?.PreviousItemAction();
                e.Handled = true;
                break;

            case Key.B:
                _engine?.GoBlack();
                e.Handled = true;
                break;

            case Key.F1:
                _engine?.ShowLogo();
                _outputWindow?.ShowLogo();
                e.Handled = true;
                break;

            case Key.Escape:
                // F6: on a single-monitor full-screen output, Esc reclaims the screen by exiting
                // full-screen (restores window chrome) rather than only content-blacking. On a
                // dual-monitor rig Esc keeps its current content-black meaning.
                if (_isSingleMonitorOutput?.Invoke() == true && _outputWindow != null)
                {
                    _outputWindow.ExitFullScreen();
                }
                else if (_engine?.IsBlacked == false)
                {
                    _engine?.GoBlack();
                }
                e.Handled = true;
                break;

            case Key.Enter:
                if (_engine != null)
                {
                    // Always (re)create/re-show the single-monitor output first (same as the GO LIVE
                    // button); the hook recreates/reshows a closed-while-live output and refreshes
                    // the _outputWindow reference. Keep GoLive() behind the state guard.
                    _onGoLive?.Invoke();
                    if (_engine.IsBlacked || !_engine.IsLive)
                    {
                        _engine.GoLive();
                    }
                    _outputWindow?.Unblack();
                }
                e.Handled = true;
                break;

            // Number keys 1-9 for jump-to-slide
            case Key.D1: _engine?.GoToSlide(0); e.Handled = true; break;
            case Key.D2: _engine?.GoToSlide(1); e.Handled = true; break;
            case Key.D3: _engine?.GoToSlide(2); e.Handled = true; break;
            case Key.D4: _engine?.GoToSlide(3); e.Handled = true; break;
            case Key.D5: _engine?.GoToSlide(4); e.Handled = true; break;
            case Key.D6: _engine?.GoToSlide(5); e.Handled = true; break;
            case Key.D7: _engine?.GoToSlide(6); e.Handled = true; break;
            case Key.D8: _engine?.GoToSlide(7); e.Handled = true; break;
            case Key.D9: _engine?.GoToSlide(8); e.Handled = true; break;

            case Key.K:
                if (Keyboard.Modifiers == ModifierKeys.Control)
                {
                    // Ctrl+K: Focus search (placeholder)
                    e.Handled = true;
                }
                break;
        }
    }

    private readonly System.Collections.ObjectModel.ObservableCollection<string> _serviceOrderItems = new();

    // Chord parser for preview rendering (mirrors actual output behavior)
    private static readonly Core.Services.ChordProParserService _previewChordParser = new();

    // --- UI Update Methods ---

    private void UpdatePreviews()
    {
        var current = _engine?.CurrentSlide;
        var next = _engine?.NextSlide;

        // AUDIENCE preview: Show what the CONGREGATION actually sees
        // When CHORDS: OFF (default) → strip [chord] brackets = clean lyrics
        // When CHORDS: ON → show with chords (as they would appear on projector)
        string audienceText = current?.Content.Text ?? "(blank)";
        if (_engine != null && !_engine.Settings.CongregationShowChords)
        {
            audienceText = _previewChordParser.StripChords(audienceText);
        }
        CurrentSlidePreview.Text = audienceText;

        // STAGE preview: Show what MUSICIANS actually see (current slide WITH chords, always)
        // Stage always shows chords — this mirrors the web stage display at /stage.
        // Render a green monospace chord row ABOVE a white monospace lyric row, using the
        // engine's GetStageText so transposition + live pitch shift apply (part b/d).
        if (current == null || _engine == null)
        {
            StagePreviewItems.ItemsSource = null;
            StagePreviewSection.Visibility = Visibility.Collapsed;
            StagePreviewPlaceholder.Visibility = Visibility.Visible;
        }
        else
        {
            string stageText = _engine.GetStageText(current, _engine.PitchShiftSemitones);
            var stageLines = _previewChordParser.BuildChordOverLyric(stageText);
            StagePreviewItems.ItemsSource = stageLines;

            if (!string.IsNullOrEmpty(current.SectionName))
            {
                StagePreviewSection.Text = current.SectionName;
                StagePreviewSection.Visibility = Visibility.Visible;
            }
            else
            {
                StagePreviewSection.Visibility = Visibility.Collapsed;
            }

            StagePreviewPlaceholder.Visibility = stageLines.Count == 0
                ? Visibility.Visible : Visibility.Collapsed;
        }

        // Update live glow on audience preview
        if (_engine != null && _engine.IsLive && !_engine.IsBlacked)
        {
            AudiencePreviewBorder.BorderBrush = new SolidColorBrush(LivePink);
            LiveBadge.Visibility = Visibility.Visible;
        }
        else
        {
            AudiencePreviewBorder.BorderBrush = new SolidColorBrush(BorderColor);
            LiveBadge.Visibility = Visibility.Collapsed;
        }
    }

    private void UpdateSlideCounter()
    {
        if (_engine != null)
        {
            SlideCounterText.Text = $"Slide {_engine.CurrentSlideIndex + 1} / {_engine.TotalSlides}";
        }
    }

    private void RefreshServiceOrderList()
    {
        _serviceOrderItems.Clear();
        ServiceOrderList.ItemsSource = _serviceOrderItems;

        var order = _engine?.CurrentServiceOrder;
        if (order == null) return;

        foreach (var item in order.Items)
        {
            var icon = item.Type switch
            {
                ServiceItemType.Song => "\u266A",
                ServiceItemType.Scripture => "\U0001F4D6",
                ServiceItemType.Announcement => "\U0001F4E2",
                ServiceItemType.Sermon => "\U0001F3A4",
                ServiceItemType.Video => "\U0001F3AC",
                ServiceItemType.Offering => "\U0001F49D",
                ServiceItemType.Prayer => "\U0001F64F",
                _ => "\u2022"
            };

            _serviceOrderItems.Add($"{icon} {item.Title}");
        }
    }

    private void RefreshSlideThumbnails()
    {
        SlideThumbnailsPanel.Children.Clear();

        if (_engine == null || _engine.TotalSlides == 0)
        {
            // Show empty state message
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        // Hide empty state message when slides exist
        EmptyStateText.Visibility = Visibility.Collapsed;

        // Get zoom size from slider (default 140 if slider not yet loaded)
        double thumbWidth = 140;
        double thumbHeight = 85;
        if (ZoomSlider != null)
        {
            thumbWidth = ZoomSlider.Value;
            thumbHeight = thumbWidth * 0.6;
        }

        // Reset tracking indices for the new item
        _lastActiveSlideIndex = _engine.CurrentSlideIndex;
        if (!_isEditMode)
        {
            _selectedSlideIndex = -1;
        }

        for (int i = 0; i < _engine.TotalSlides; i++)
        {
            var slide = _engine.CurrentSlides[i];
            int capturedIndex = i;
            bool isActive = (i == _engine.CurrentSlideIndex);
            bool isSelected = (_isEditMode && i == _selectedSlideIndex);

            var border = new Border
            {
                Width = thumbWidth,
                Height = thumbHeight,
                Background = new SolidColorBrush(PanelBg),
                BorderBrush = isActive
                    ? new SolidColorBrush(AccentCyan)
                    : isSelected
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0088AA"))
                        : new SolidColorBrush(BorderColor),
                BorderThickness = new Thickness(isActive ? 2 : isSelected ? 2 : 1),
                Margin = new Thickness(4),
                CornerRadius = new CornerRadius(6),
                Cursor = Cursors.Hand
            };

            var grid = new Grid();

            // Calculate font sizes proportional to thumbnail size (WYSIWYG scaling)
            double scaleFactor = thumbWidth / 140.0; // 140 is the default/base size
            double sectionFontSize = Math.Max(8, 8 * scaleFactor);
            double mainFontSize = Math.Max(9, 11 * scaleFactor);
            double numFontSize = Math.Max(8, 8 * scaleFactor);
            int maxTextLength = thumbWidth > 300 ? 200 : thumbWidth > 200 ? 80 : 40;

            // Section label (top-left)
            var sectionLabel = new TextBlock
            {
                Text = slide.SectionName ?? "",
                FontSize = sectionFontSize,
                Foreground = new SolidColorBrush(TextSecondary),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = System.Windows.VerticalAlignment.Top,
                Margin = new Thickness(6, 4, 0, 0)
            };

            // Slide text (center) — scales with zoom for WYSIWYG preview
            // Strip chords for clean readability (thumbnails show what audience sees)
            string slideText = _previewChordParser.StripChords(slide.Content.Text ?? "");
            var text = new TextBlock
            {
                Text = TruncateText(slideText, maxTextLength),
                FontSize = mainFontSize,
                Foreground = new SolidColorBrush(Colors.White),
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = System.Windows.TextAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(8, sectionFontSize + 8, 8, numFontSize + 8)
            };

            // Slide number (bottom-right)
            var slideNum = new TextBlock
            {
                Text = (i + 1).ToString(),
                FontSize = numFontSize,
                Foreground = new SolidColorBrush(TextMuted),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 6, 4)
            };

            grid.Children.Add(sectionLabel);
            grid.Children.Add(text);
            grid.Children.Add(slideNum);

            border.Child = grid;

            // Click behavior depends on edit mode
            border.MouseLeftButtonDown += (s, ev) =>
            {
                if (_isEditMode)
                {
                    // In edit mode, select (highlight) without firing live
                    _selectedSlideIndex = capturedIndex;
                    UpdateSlideThumbnailHighlights();
                }
                else
                {
                    // Performance mode: fire live immediately
                    _engine?.GoToSlide(capturedIndex);
                }
            };

            SlideThumbnailsPanel.Children.Add(border);
        }
    }

    /// <summary>
    /// Efficiently update only the border highlights without rebuilding all children.
    /// Called on slide navigation when the slide set itself hasn't changed.
    /// </summary>
    private void UpdateSlideThumbnailHighlights()
    {
        if (_engine == null) return;

        int currentIndex = _engine.CurrentSlideIndex;

        for (int i = 0; i < SlideThumbnailsPanel.Children.Count; i++)
        {
            if (SlideThumbnailsPanel.Children[i] is Border border)
            {
                bool isActive = (i == currentIndex);
                bool isSelected = (_isEditMode && i == _selectedSlideIndex);

                border.BorderBrush = isActive
                    ? new SolidColorBrush(AccentCyan)
                    : isSelected
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0088AA"))
                        : new SolidColorBrush(BorderColor);
                border.BorderThickness = new Thickness(isActive ? 2 : isSelected ? 2 : 1);
            }
        }

        _lastActiveSlideIndex = currentIndex;
    }

    /// <summary>
    /// Update the backtrack status panel (called externally by the integration layer).
    /// </summary>
    public void UpdateBacktrackStatus(bool isConnected, string songTitle, string sectionName)
    {
        Dispatcher.Invoke(() =>
        {
            BacktrackStatusText.Text = isConnected ? "Connected" : "Not Connected";
            BacktrackStatusText.Foreground = new SolidColorBrush(
                isConnected ? ConnectedGreen : WarningOrange);
            BacktrackSongText.Text = songTitle;
            BacktrackSectionText.Text = sectionName;
        });
    }

    /// <summary>
    /// Update the connected devices count indicator and stage connection status.
    /// </summary>
    public void UpdateConnectedDevices(int count)
    {
        Dispatcher.Invoke(() =>
        {
            ConnectedDevicesCount.Text = count.ToString();
            UpdateStageConnectionIndicator(count > 0);
        });
    }

    /// <summary>
    /// Update the stage preview CONNECTED/DISCONNECTED indicator based on actual connection state.
    /// </summary>
    private void UpdateStageConnectionIndicator(bool isConnected)
    {
        if (isConnected)
        {
            StageConnectedDot.Fill = new SolidColorBrush(ConnectedGreen);
            StageConnectedText.Text = "CONNECTED";
            StageConnectedText.Foreground = new SolidColorBrush(ConnectedGreen);
        }
        else
        {
            StageConnectedDot.Fill = new SolidColorBrush(TextMuted);
            StageConnectedText.Text = "DISCONNECTED";
            StageConnectedText.Foreground = new SolidColorBrush(TextMuted);
        }
    }

    /// <summary>
    /// Populate the theme selector dropdown from ThemeManager.
    /// </summary>
    private void PopulateThemeSelector()
    {
        ThemeSelector.Items.Clear();

        if (_themeManager == null) return;

        foreach (var theme in _themeManager.AvailableThemes)
        {
            ThemeSelector.Items.Add(theme.Name);
        }

        var currentIndex = _themeManager.AvailableThemes.FindIndex(t => t.Id == _themeManager.CurrentTheme.Id);
        if (currentIndex >= 0)
            ThemeSelector.SelectedIndex = currentIndex;
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text)) return "";
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }

    // --- Navigation Tab Handlers ---

    private void TabServices_Click(object sender, RoutedEventArgs e) => SwitchToTab("Services");
    private void TabSongs_Click(object sender, RoutedEventArgs e) => SwitchToTab("Songs");
    private void TabScripture_Click(object sender, RoutedEventArgs e) => SwitchToTab("Scripture");
    private void TabMedia_Click(object sender, RoutedEventArgs e) => SwitchToTab("Media");

    private void TabTools_Click(object sender, RoutedEventArgs e)
    {
        // Open the ContextMenu dropdown
        if (TabTools.ContextMenu != null)
        {
            TabTools.ContextMenu.PlacementTarget = TabTools;
            TabTools.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            TabTools.ContextMenu.IsOpen = true;
        }
    }

    private void SwitchToTab(string tabName)
    {
        _activeTab = tabName;

        // Hide all panels
        ServicesPanel.Visibility = Visibility.Collapsed;
        SongsPanel.Visibility = Visibility.Collapsed;
        ScripturePanel.Visibility = Visibility.Collapsed;
        MediaPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;

        // Reset all tab button styles
        SetTabInactive(TabServices);
        SetTabInactive(TabSongs);
        SetTabInactive(TabScripture);
        SetTabInactive(TabMedia);

        try
        {
            switch (tabName)
            {
                case "Services":
                    ServicesPanel.Visibility = Visibility.Visible;
                    SetTabActive(TabServices);
                    break;

                case "Songs":
                    if (_songLibraryPanel == null)
                    {
                        _songLibraryPanel = new SongLibraryPanel();
                        if (_songLibraryService != null)
                            _songLibraryPanel.SongLibraryService = _songLibraryService;
                        _songLibraryPanel.SongSelected += OnSongSelected;
                    }
                    SongsPanelContent.Content = _songLibraryPanel;
                    SongsPanel.Visibility = Visibility.Visible;
                    SetTabActive(TabSongs);
                    break;

                case "Scripture":
                    if (_scriptureBrowserPanel == null)
                    {
                        _scriptureBrowserPanel = new ScriptureBrowserPanel();
                        if (_bibleProvider != null)
                            _scriptureBrowserPanel.BibleProvider = _bibleProvider;
                    }
                    ScripturePanelContent.Content = _scriptureBrowserPanel;
                    ScripturePanel.Visibility = Visibility.Visible;
                    SetTabActive(TabScripture);
                    break;

                case "Media":
                    if (_mediaLibraryPanel == null)
                    {
                        _mediaLibraryPanel = new MediaLibraryPanel();
                        if (_mediaLibraryService != null)
                            _mediaLibraryPanel.MediaLibraryService = _mediaLibraryService;
                        _mediaLibraryPanel.ImportRequested += OnMediaImportRequested;
                    }
                    MediaPanelContent.Content = _mediaLibraryPanel;
                    MediaPanel.Visibility = Visibility.Visible;
                    SetTabActive(TabMedia);
                    break;
            }
        }
        catch (Exception ex)
        {
            // Show error panel if a panel fails to load
            ErrorPanelMessage.Text = $"Failed to load {tabName} panel: {ex.Message}";
            ErrorPanel.Visibility = Visibility.Visible;
        }
    }

    private void OnSongSelected(object? sender, SongListItem songItem)
    {
        // Load the selected song into the presenter engine (Songs-tab standalone path).
        // The item's Id is produced from Song.FolderPath, but folder-scan vs. registry
        // paths can differ by casing/separators — resolve robustly (normalized path,
        // then folder name, then title) instead of relying on an exact string match.
        if (_engine == null || _songManager == null) return;

        var song = SongPathResolver.Resolve(_songManager.Songs, songItem.Id, songItem.Title);
        if (song != null)
        {
            _engine.LoadStandaloneSong(song);
        }
        else
        {
            // Non-fatal: nothing resolved — surface it instead of silently doing nothing.
            System.Diagnostics.Debug.WriteLine(
                $"OnSongSelected: could not resolve song (Id='{songItem.Id}', Title='{songItem.Title}').");
        }
    }

    private void OnMediaImportRequested(object? sender, EventArgs e)
    {
        if (_mediaLibraryService == null) return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import Media File",
            Filter = "Media Files (*.png;*.jpg;*.jpeg;*.gif;*.webp;*.mp4;*.mov;*.webm;*.avi)|*.png;*.jpg;*.jpeg;*.gif;*.webp;*.mp4;*.mov;*.webm;*.avi|All Files (*.*)|*.*",
            Multiselect = true
        };

        if (dialog.ShowDialog() == true)
        {
            _ = ImportMediaFilesAsync(dialog.FileNames);
        }
    }

    private async Task ImportMediaFilesAsync(string[] filePaths)
    {
        if (_mediaLibraryService == null || _mediaLibraryPanel == null) return;

        foreach (var filePath in filePaths)
        {
            try
            {
                var item = await _mediaLibraryService.ImportAsync(filePath);
                _mediaLibraryPanel.Items.Add(new MediaItem
                {
                    Id = item.Id,
                    Name = item.Name,
                    MediaType = item.MediaType.ToString(),
                    FilePath = item.FilePath,
                    FileSize = item.FileSize,
                    DateAdded = item.DateAdded,
                    Tags = item.Tags
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Media import failed for {filePath}: {ex.Message}");
            }
        }
    }

    private void SetTabActive(Button tab)
    {
        tab.Foreground = new SolidColorBrush(AccentCyan);
        tab.BorderBrush = new SolidColorBrush(AccentCyan);
    }

    private void SetTabInactive(Button tab)
    {
        tab.Foreground = new SolidColorBrush(TextSecondary);
        tab.BorderBrush = new SolidColorBrush(Colors.Transparent);
    }

    // --- Tools Menu Handlers ---

    private void ToolAlert_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new AlertToolDialog();
            dialog.Owner = this;
            if (_alertService != null)
                dialog.AlertService = _alertService;
            dialog.AlertFired += (s, template) =>
            {
                _outputWindow?.ShowAlert(template, 5000);
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowToolError("Alert", ex.Message);
        }
    }

    private void ToolCountdown_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new CountdownToolDialog();
            dialog.Owner = this;
            if (_countdownService != null)
                dialog.CountdownService = _countdownService;
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowToolError("Countdown", ex.Message);
        }
    }

    private void ToolSearch_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var searchPanel = new GlobalSearchPanel();
            if (_searchService != null)
                searchPanel.SearchService = _searchService;
            var searchWindow = new Window
            {
                Title = "Global Search",
                Width = 600,
                Height = 500,
                MinWidth = 400,
                MinHeight = 350,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F1117")),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Content = searchPanel
            };
            searchWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowToolError("Search", ex.Message);
        }
    }

    private void ToolCcliReport_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new CcliReportDialog();
            dialog.Owner = this;
            if (_songUsageService != null)
            {
                _ = LoadCcliReportDataAsync(dialog);
            }
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowToolError("CCLI Report", ex.Message);
        }
    }

    private async Task LoadCcliReportDataAsync(CcliReportDialog dialog)
    {
        if (_songUsageService == null) return;

        try
        {
            var records = await _songUsageService.GetAllAsync();
            var reportItems = records
                .GroupBy(r => r.SongTitle)
                .Select(g => new CcliReportItem
                {
                    Title = g.Key,
                    Artist = g.First().Artist,
                    TimesPlayed = g.Count(),
                    LastPlayed = g.Max(r => r.DatePlayed)
                })
                .OrderByDescending(i => i.TimesPlayed)
                .ToList();

            dialog.SetReportData(reportItems);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CCLI report loading failed: {ex.Message}");
        }
    }

    private void ShowToolError(string toolName, string message)
    {
        MessageBox.Show(
            $"Failed to open {toolName}: {message}",
            "Error",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    /// <summary>
    /// Export the current service order to a text file.
    /// </summary>
    public void ExportServiceOrder_Click(object sender, RoutedEventArgs e)
    {
        if (_exportService == null || _serviceOrderManager?.CurrentServiceOrder == null)
        {
            MessageBox.Show("No service order is currently loaded.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            DefaultExt = "txt",
            FileName = $"ServiceOrder_{DateTime.Now:yyyyMMdd}.txt"
        };

        if (dialog.ShowDialog() == true)
        {
            _ = ExportServiceOrderAsync(dialog.FileName);
        }
    }

    private async Task ExportServiceOrderAsync(string outputPath)
    {
        if (_exportService == null || _serviceOrderManager?.CurrentServiceOrder == null) return;

        try
        {
            await _exportService.ExportServiceOrderTextAsync(
                _serviceOrderManager.CurrentServiceOrder,
                includeLyrics: true,
                outputPath);
            MessageBox.Show("Service order exported successfully.", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
