using System.IO;
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
using WorshipPlayer.Presenter.Views.Home;
using WorshipPlayer.Presenter.Views.SongLibrary;
using WorshipPlayer.Presenter.Views.Scripture;
using WorshipPlayer.Presenter.Views.MediaLibrary;
using WorshipPlayer.Presenter.Views.Tools;
using WorshipPlayer.Presenter.Views.Settings;
using WorshipPlayer.Presenter.Views.ServiceBuilder;
using WorshipPlayer.Presenter.Views.Templates;

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
    private async Task<bool> LoadServiceOrderAsync(string path)
    {
        if (_serviceOrderManager == null) return false;
        try
        {
            var loaded = await _serviceOrderManager.LoadAsync(path);
            if (loaded == null)
            {
                MessageBox.Show(this, "The selected service order could not be loaded (missing or invalid file).",
                    "Load Service", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            return loaded != null;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Could not load the service order:\n{ex.Message}",
                "Load Service", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
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
    // J4: constructed lazily from _songUsageService (construct-from-dependency pattern) so the CCLI
    // report dialog loads/exports through the shared Core CcliReportService — no longer test-only.
    private CcliReportService? _ccliReportService;
    private IBibleProvider? _bibleProvider;
    private ServiceOrderManager? _serviceOrderManager;
    private SongManager? _songManager;
    private ITemplateService? _templateService;

    // Navigation tab state
    private string _activeTab = "Services";
    private SongLibraryPanel? _songLibraryPanel;
    private ScriptureBrowserPanel? _scriptureBrowserPanel;
    private MediaLibraryPanel? _mediaLibraryPanel;

    // Home / Services-list screens (FEAT-002). The views are passive renderers; the host
    // owns all service mutations and re-scans via Core after each.
    private HomeView? _homeView;
    private ServicesListView? _servicesListView;
    private string? _currentServicePath;

    /// <summary>Normalized full-path equality for scanned-vs-derived service file paths.</summary>
    private static bool SamePath(string? a, string? b) =>
        a != null && b != null &&
        string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

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

        if (_scriptureBrowserPanel != null)
        {
            _scriptureBrowserPanel.PresentSlidesRequested -= OnScripturePresentRequested;
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

        // FEAT-002: land on the Home screen. Synchronous view setup inline (Initialize is a
        // sync void and cannot await); the services scan is fired-and-forgotten but observes
        // its own exceptions via RefreshServiceListingInitialAsync.
        EnsureHomeView();
        HomePanelContent.Content = _homeView;
        HomePanel.Visibility = Visibility.Visible;
        ServicesListPanel.Visibility = Visibility.Collapsed;
        ServicesPanel.Visibility = Visibility.Collapsed;

        _ = RefreshServiceListingInitialAsync();
    }

    /// <summary>Lazily create and wire the Home view (idempotent).</summary>
    private void EnsureHomeView()
    {
        if (_homeView != null) return;

        _homeView = new HomeView();
        _homeView.TourRequested += (_, _) => { /* placeholder, no-op */ };
        _homeView.ServicesRequested += (_, _) => ShowServicesList();
        _homeView.SongsRequested += (_, _) => SwitchToTab("Songs");
        _homeView.ScriptureRequested += (_, _) => SwitchToTab("Scripture");
        _homeView.MediaRequested += (_, _) => SwitchToTab("Media");
        _homeView.SlidesRequested += (_, _) => ShowServiceEditor();
        _homeView.AddServiceRequested += OnNewServiceRequested;
        _homeView.OpenServiceRequested += OnOpenServiceRequested;
        _homeView.RenameServiceRequested += OnRenameServiceRequested;
        _homeView.DeleteServiceRequested += OnDeleteServiceRequested;
    }

    /// <summary>Lazily create and wire the Services-list view (idempotent).</summary>
    private void EnsureServicesListView()
    {
        if (_servicesListView != null) return;

        _servicesListView = new ServicesListView();
        _servicesListView.NewServiceRequested += OnNewServiceRequested;
        _servicesListView.OpenServiceRequested += OnOpenServiceRequested;
        _servicesListView.EditServiceRequested += OnEditServiceRequested;
        _servicesListView.RenameServiceRequested += OnRenameServiceRequested;
        _servicesListView.DeleteServiceRequested += OnDeleteServiceRequested;
    }

    /// <summary>
    /// Fire-and-forget wrapper for the initial services scan. Observes its own exceptions so
    /// an unobserved task exception can never crash the window.
    /// </summary>
    private async Task RefreshServiceListingInitialAsync()
    {
        try { await RefreshServiceListingAsync(); }
        catch (Exception ex) { ShowToolError("Services", ex.Message); }
    }

    /// <summary>
    /// Re-scan the ServiceOrders folder via Core and rebind both tables. Non-fatal: any error
    /// is surfaced via ShowToolError and the previously-bound rows remain. Called on initial
    /// load and after every create/rename/delete.
    /// </summary>
    private async Task RefreshServiceListingAsync()
    {
        if (_serviceOrderManager == null) return;
        try
        {
            var files = await _serviceOrderManager.ListServiceFilesAsync();
            var rows = ServiceListingBuilder.Build(files, DateTime.UtcNow);
            _homeView?.SetServices(rows);
            _servicesListView?.SetServices(rows);
        }
        catch (Exception ex)
        {
            ShowToolError("Services", ex.Message);
        }
    }

    /// <summary>
    /// Show the Services-list table (the Services top-nav tab). Lazily creates the view,
    /// hosts it, hides the other center panels, marks the tab active, and refreshes the list.
    /// </summary>
    private async void ShowServicesList()
    {
        try
        {
            EnsureServicesListView();
            ServicesListPanelContent.Content = _servicesListView;

            HomePanel.Visibility = Visibility.Collapsed;
            ServicesPanel.Visibility = Visibility.Collapsed;
            SongsPanel.Visibility = Visibility.Collapsed;
            ScripturePanel.Visibility = Visibility.Collapsed;
            MediaPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Collapsed;
            ServicesListPanel.Visibility = Visibility.Visible;

            _activeTab = "Services";
            SetTabInactive(TabLive);
            SetTabInactive(TabSongs);
            SetTabInactive(TabScripture);
            SetTabInactive(TabMedia);
            SetTabActive(TabServices);

            await RefreshServiceListingAsync();
        }
        catch (Exception ex)
        {
            ShowToolError("Services", ex.Message);
        }
    }

    /// <summary>
    /// Reveal the slide-editing grid (ServicesPanel) when a service is opened. For a non-empty
    /// service, OnItemChanged has already set the header via the GoToItem(0) chain; for an empty
    /// or none-current service we write the header from the current order so no stale title shows.
    /// </summary>
    private void ShowServiceEditor()
    {
        HomePanel.Visibility = Visibility.Collapsed;
        ServicesListPanel.Visibility = Visibility.Collapsed;
        SongsPanel.Visibility = Visibility.Collapsed;
        ScripturePanel.Visibility = Visibility.Collapsed;
        MediaPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        ServicesPanel.Visibility = Visibility.Visible;

        // Showing the slide-editing grid IS the Live presentation surface, so reflect that in
        // the top nav: the Live tab becomes active (the Live-status dot is driven separately
        // by engine IsLive state, not by tab selection).
        _activeTab = "Live";
        SetTabActive(TabLive);
        SetTabInactive(TabServices);
        SetTabInactive(TabSongs);
        SetTabInactive(TabScripture);
        SetTabInactive(TabMedia);

        var order = _serviceOrderManager?.CurrentServiceOrder;
        if (order == null || order.Items.Count == 0)
        {
            CurrentItemTitle.Text = order?.Name ?? "";
            CurrentItemSubtitle.Text = order?.ServiceType ?? "";
        }
    }

    // --- FEAT-002 view event handlers (async void at the WPF boundary) ---

    private async void OnOpenServiceRequested(object? sender, string path)
    {
        try
        {
            if (await LoadServiceOrderAsync(path))
            {
                _currentServicePath = path;
                ShowServiceEditor();
                RefreshServiceOrderList();
            }
        }
        catch (Exception ex)
        {
            ShowToolError("Open Service", ex.Message);
        }
    }

    private async void OnNewServiceRequested(object? sender, EventArgs e)
    {
        try
        {
            if (_serviceOrderManager == null) return;

            // Open the Add Service Item dialog in "new service" mode so it shows the
            // Service name + date fields and creates a brand-new service order.
            var dialog = new AddServiceItemDialog { Owner = this, CreateNewService = true };
            dialog.ServiceOrderManager = _serviceOrderManager;
            dialog.SongLibrary = _songManager?.Songs ?? (IReadOnlyList<Song>)Array.Empty<Song>();
            dialog.ItemAdded += (s, item) => { };
            dialog.ShowDialog();

            if (_serviceOrderManager.CurrentServiceOrder != null)
            {
                await SaveServiceOrderAsync();
                _currentServicePath = _serviceOrderManager.GetFilePathFor(_serviceOrderManager.CurrentServiceOrder);
                ShowServiceEditor();
                RefreshServiceOrderList();
                await RefreshServiceListingAsync();
            }
        }
        catch (Exception ex)
        {
            ShowToolError("New Service", ex.Message);
        }
    }

    private async void OnRenameServiceRequested(object? sender, string path)
    {
        try
        {
            if (!SamePath(path, _currentServicePath) || _serviceOrderManager?.CurrentServiceOrder == null)
                return;

            var order = _serviceOrderManager.CurrentServiceOrder;
            var dlg = new ServiceInfoDialog { Owner = this };
            dlg.Initialize(order.Name, order.Date, order.StartTime);
            if (dlg.ShowDialog() != true || !dlg.Confirmed) return;

            order.Name = dlg.ServiceName;
            order.Date = dlg.ServiceDate;
            order.StartTime = dlg.StartTime;
            await SaveServiceOrderAsync(path);
            RefreshServiceOrderList();
            await RefreshServiceListingAsync();
        }
        catch (Exception ex)
        {
            ShowToolError("Rename Service", ex.Message);
        }
    }

    private async void OnEditServiceRequested(object? sender, string path)
    {
        try
        {
            if (_serviceOrderManager == null) return;

            // Ensure the clicked service is the loaded one so we edit the right order.
            if (!SamePath(path, _currentServicePath))
            {
                if (!await LoadServiceOrderAsync(path)) return;
                _currentServicePath = path;
                ShowServiceEditor();
                RefreshServiceOrderList();
            }

            var order = _serviceOrderManager.CurrentServiceOrder;
            if (order == null) return;

            var dlg = new ServiceInfoDialog { Owner = this };
            dlg.Initialize(order.Name, order.Date, order.StartTime);
            if (dlg.ShowDialog() != true || !dlg.Confirmed) return;

            order.Name = dlg.ServiceName;
            order.Date = dlg.ServiceDate;
            order.StartTime = dlg.StartTime;
            await SaveServiceOrderAsync(_currentServicePath);
            RefreshServiceOrderList();
            await RefreshServiceListingAsync();
        }
        catch (Exception ex)
        {
            ShowToolError("Edit Service", ex.Message);
        }
    }

    private async void OnDeleteServiceRequested(object? sender, string path)
    {
        try
        {
            var result = MessageBox.Show(this,
                "Delete this service? This cannot be undone.",
                "Delete Service", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (result != MessageBoxResult.Yes) return;

            try { File.Delete(path); }
            catch (Exception ex) { ShowToolError("Delete Service", ex.Message); }

            await RefreshServiceListingAsync();
            if (SamePath(path, _currentServicePath))
                _currentServicePath = null;
        }
        catch (Exception ex)
        {
            ShowToolError("Delete Service", ex.Message);
        }
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
        SongManager? songManager,
        ITemplateService? templateService)
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
        _templateService = templateService;

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

        // Now that the service-order manager is injected, populate the Home + Services
        // listings. Initialize() runs before SetServices(), so the earlier
        // RefreshServiceListingInitialAsync() call saw a null manager and returned
        // without loading anything — hence the empty "Upcoming services" table. Refresh
        // here so existing services show up immediately on the Home screen.
        if (_serviceOrderManager != null)
            _ = RefreshServiceListingInitialAsync();
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

            // 7.2: route a Video service item to full-screen playback on the output. The pure
            // resolver decides whether this item plays video and with which path; a non-video item
            // leaves video mode so lyrics/slides resume. Slides for a video item are still loaded by
            // the engine but OutputWindow.ShowSlide no-ops while in video mode.
            var videoRoute = VideoRouteResolver.Resolve(e.NewItem);
            if (videoRoute.PlayVideo)
            {
                _outputWindow?.ShowVideo(videoRoute.VideoPath);
            }
            else if (_outputWindow?.IsVideoMode == true)
            {
                _outputWindow.StopVideo();
            }

            // BUG A: ensure the sidebar reflects the loaded service. On startup the service order
            // is auto-loaded asynchronously (ServiceOrderManager.LoadAsync) AFTER the operator
            // window has already run its initial RefreshServiceOrderList() against an empty order.
            // The load path ends in GoToItem(0) -> ItemChanged, so refreshing the list here
            // repopulates it once a service is actually loaded (covers both auto-load and manual
            // Load Service). The list is only rebuilt when its item count no longer matches, so the
            // user's current selection is preserved for ordinary item-to-item navigation.
            // C2: the sidebar may contain group-header rows (Item == null) in addition to item
            // rows, so compare the ITEM-row count (not total row count) against the order.
            var orderItemCount = _engine?.CurrentServiceOrder?.Items.Count ?? 0;
            var itemRowCount = _serviceOrderItems.Count(d => d.Item != null);
            if (itemRowCount != orderItemCount)
            {
                RefreshServiceOrderList();
            }

            RefreshSlideThumbnails();
            UpdatePreviews();
            UpdateSlideCounter();
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
                UpdateLiveTabDot(false);
            }
            else if (e.IsLive)
            {
                LiveButton.Content = "LIVE";
                LiveButton.Background = new SolidColorBrush(LivePink);
                LiveBadge.Visibility = Visibility.Visible;
                AudiencePreviewBorder.BorderBrush = new SolidColorBrush(LivePink);
                UpdateLiveTabDot(true);
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

    private async void NewService_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // "+ Add a new service": open the dialog in new-service mode so it shows
            // the Service name + date fields and creates a brand-new service order
            // (even when a service is already auto-loaded).
            var dialog = new AddServiceItemDialog { CreateNewService = true };
            dialog.Owner = this;
            if (_serviceOrderManager != null)
                dialog.ServiceOrderManager = _serviceOrderManager;
            dialog.SongLibrary = _songManager?.Songs ?? (IReadOnlyList<Song>)Array.Empty<Song>();
            dialog.ItemAdded += async (s, item) =>
            {
                await SaveServiceOrderAsync();
                RefreshServiceOrderList();
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowToolError("Add Service Item", ex.Message);
        }
    }

    private async void EditService_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var order = _serviceOrderManager?.CurrentServiceOrder;
            if (order == null)
            {
                MessageBox.Show("No service is loaded. Click '+ Add Item' to create one first.",
                    "Edit Service", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var infoDialog = new ServiceInfoDialog { Owner = this };
            infoDialog.Initialize(order.Name, order.Date, order.StartTime);
            if (infoDialog.ShowDialog() != true || !infoDialog.Confirmed)
                return;

            order.Name = infoDialog.ServiceName;
            order.Date = infoDialog.ServiceDate;
            order.StartTime = infoDialog.StartTime;

            await SaveServiceOrderAsync();
            RefreshServiceOrderList();
        }
        catch (Exception ex)
        {
            ShowToolError("Edit Service", ex.Message);
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

    /// <summary>
    /// Lightweight display wrapper so the sidebar ListBox can show icon + title while
    /// keeping a reference back to the underlying ServiceItem (selected index maps to
    /// CurrentServiceOrder.Items[i]).
    /// </summary>
    private sealed class ServiceItemDisplay
    {
        public string Icon { get; set; } = "";
        public string Title { get; set; } = "";

        // Null for group-header rows; non-null for item rows. Selection/navigation and the
        // sidebar sync check both key off this (null => a header row, skipped).
        public ServiceItem? Item { get; set; }

        // C2 group-header fields. Group is non-null only when IsGroupHeader is true.
        public bool IsGroupHeader { get; set; }
        public ServiceGroup? Group { get; set; }

        public Visibility HeaderVisibility => IsGroupHeader ? Visibility.Visible : Visibility.Collapsed;
        public Visibility ItemVisibility => IsGroupHeader ? Visibility.Collapsed : Visibility.Visible;

        // ▼ expanded, ► collapsed. The "Ungrouped" header (Group == null) is not collapsible,
        // so it shows no chevron.
        public string CollapseGlyph => Group == null ? "" : (Group.IsCollapsed ? "\u25BA" : "\u25BC");
    }

    private readonly System.Collections.ObjectModel.ObservableCollection<ServiceItemDisplay> _serviceOrderItems = new();

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
        // BUG B: suppress the SelectionChanged handler while we rebuild the list. Clearing and
        // repopulating the backing collection raises SelectionChanged, which must not re-enter
        // GoToItem during a programmatic refresh.
        _suppressServiceOrderSelection = true;
        try
        {
            _serviceOrderItems.Clear();
            ServiceOrderList.ItemsSource = _serviceOrderItems;

            var order = _engine?.CurrentServiceOrder;
            if (order == null) return;

            // C2: when no groups exist, render items flat exactly as before (legacy / no-group
            // behavior). Otherwise render each group as a collapsible header followed by its
            // (uncollapsed) item rows, then an "Ungrouped" section for any leftover items.
            if (order.Groups.Count == 0)
            {
                foreach (var item in order.Items)
                    _serviceOrderItems.Add(MakeItemRow(item));
            }
            else
            {
                var byId = order.Items.ToDictionary(i => i.Id);

                foreach (var group in order.Groups.OrderBy(g => g.Order))
                {
                    _serviceOrderItems.Add(new ServiceItemDisplay
                    {
                        IsGroupHeader = true,
                        Group = group,
                        Title = group.Name
                    });

                    if (group.IsCollapsed) continue;

                    foreach (var id in group.Items)
                        if (byId.TryGetValue(id, out var item))
                            _serviceOrderItems.Add(MakeItemRow(item));
                }

                // Ungrouped bucket: items not in any group, in ServiceOrder.Items order.
                var ungroupedIds = _serviceOrderManager?.GetUngroupedItemIds()
                    ?? (IReadOnlyList<string>)System.Array.Empty<string>();
                if (ungroupedIds.Count > 0)
                {
                    _serviceOrderItems.Add(new ServiceItemDisplay
                    {
                        IsGroupHeader = true,
                        Group = null,
                        Title = "Ungrouped"
                    });

                    foreach (var id in ungroupedIds)
                        if (byId.TryGetValue(id, out var item))
                            _serviceOrderItems.Add(MakeItemRow(item));
                }
            }
        }
        finally
        {
            _suppressServiceOrderSelection = false;
            // FEAT-002: show the dashed sidebar empty-state only when no items are listed.
            // C2: a group header can occupy a row with zero service items, so key the empty
            // state off the actual item count, not the row count (which includes headers).
            if (SidebarEmptyState != null)
                SidebarEmptyState.Visibility = (_engine?.CurrentServiceOrder?.Items.Count ?? 0) == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Build an item (non-header) display row for the sidebar.
    /// </summary>
    private static ServiceItemDisplay MakeItemRow(ServiceItem item)
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

        return new ServiceItemDisplay
        {
            Icon = icon,
            Title = item.Title,
            Item = item
        };
    }

    /// <summary>
    /// Persist the current service order to disk. Single reuse point for every
    /// service mutation (add/edit/delete). No-ops when nothing is loaded.
    /// </summary>
    private async Task SaveServiceOrderAsync(string? path = null)
    {
        if (_serviceOrderManager?.CurrentServiceOrder == null) return;
        try
        {
            await _serviceOrderManager.SaveAsync(path);
        }
        catch (Exception ex)
        {
            ShowToolError("Save Service", ex.Message);
        }
    }

    // BUG B: guards the SelectionChanged handler against selection changes that we cause
    // ourselves while rebuilding the list (RefreshServiceOrderList clears the ItemsSource, which
    // raises SelectionChanged). Without this a refresh would re-enter GoToItem and fight the
    // navigation that triggered the refresh.
    private bool _suppressServiceOrderSelection;

    // C3 Bulk edit: when true the sidebar is in multi-select mode (ListBox SelectionMode = Extended)
    // and single-click navigation is suppressed so the user can build a selection set to batch-edit.
    private bool _bulkEditMode;

    /// <summary>
    /// BUG B: single-click a service item on the left to make it the current item. Navigating via
    /// ServiceOrderManager.GoToItem fires ItemChanged -> PresenterEngine.OnServiceItemChanged
    /// (loads that item's slides + fires SlideChanged), which this window handles to update the
    /// previews, slide counter, and live-slide highlight. RefreshSlideThumbnails repaints the
    /// center grid with the newly selected item's slides.
    /// </summary>
    private void ServiceOrderList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressServiceOrderSelection) return;
        // C3: while bulk-edit multi-select is on, selecting rows must NOT drive single-item
        // navigation — the user is building a selection set for a batch apply.
        if (_bulkEditMode) return;
        if (ServiceOrderList.SelectedItem is not ServiceItemDisplay display) return;
        // C2: group-header rows carry a null Item — don't navigate when one is selected.
        if (display.Item == null) return;

        var items = _engine?.CurrentServiceOrder?.Items;
        if (items == null) return;

        int index = items.IndexOf(display.Item);
        if (index >= 0)
        {
            _serviceOrderManager?.GoToItem(index);
            RefreshSlideThumbnails();
        }
    }

    private void ServiceOrderList_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        EditSelectedServiceItem();
    }

    private void EditServiceItem_Click(object sender, RoutedEventArgs e)
    {
        EditSelectedServiceItem();
    }

    private void EditSelectedServiceItem()
    {
        try
        {
            if (_serviceOrderManager == null) return;
            if (ServiceOrderList.SelectedItem is not ServiceItemDisplay display) return;
            if (display.Item == null) return; // C2: ignore group-header rows.

            var dialog = new AddServiceItemDialog
            {
                Owner = this,
                ServiceOrderManager = _serviceOrderManager,
                SongLibrary = _songManager?.Songs ?? (IReadOnlyList<Song>)Array.Empty<Song>(),
                ItemToEdit = display.Item
            };
            dialog.ItemEdited += async (s, item) =>
            {
                await SaveServiceOrderAsync();
                RefreshServiceOrderList();
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowToolError("Edit Service Item", ex.Message);
        }
    }

    private void ItemSettings_Click(object sender, RoutedEventArgs e)
    {
        OpenSelectedItemSettings();
    }

    /// <summary>
    /// Open the per-item auto-advance settings for the selected service item (roadmap C4).
    /// Mirrors <see cref="EditSelectedServiceItem"/>: read the selection, edit the item's
    /// AutoAdvanceSettings in a modal, then persist via the existing ServiceOrder save path.
    /// </summary>
    private async void OpenSelectedItemSettings()
    {
        try
        {
            if (_serviceOrderManager?.CurrentServiceOrder == null) return;
            if (ServiceOrderList.SelectedItem is not ServiceItemDisplay display) return;
            if (display.Item == null) return; // C2: ignore group-header rows.

            var window = new ItemSettingsWindow(display.Item, _serviceOrderManager.CurrentServiceOrder.Items)
            {
                Owner = this
            };

            if (window.ShowDialog() == true && window.Result != null)
            {
                display.Item.AutoAdvanceSettings = window.Result;
                await SaveServiceOrderAsync();
                RefreshServiceOrderList();
            }
        }
        catch (Exception ex)
        {
            ShowToolError("Item Settings", ex.Message);
        }
    }

    // ─── C3 Bulk edit: multi-select toggle + batch apply ──────────────────────────
    // Coexists with C2 grouped rendering: the toggle only flips the ListBox selection mode and
    // reveals the "Apply to selected…" action; it does not change how rows are rendered. Group
    // headers (Item == null) are filtered out of the selected set before applying.

    /// <summary>Enter bulk-edit mode: allow multi-selection and reveal the batch-apply action.</summary>
    private void BulkEditToggle_Checked(object sender, RoutedEventArgs e)
    {
        _bulkEditMode = true;
        ServiceOrderList.SelectionMode = SelectionMode.Extended;
        ServiceOrderList.UnselectAll();
        if (ApplyToSelectedBtn != null)
            ApplyToSelectedBtn.Visibility = Visibility.Visible;
    }

    /// <summary>Leave bulk-edit mode: restore single-select navigation and hide the batch-apply action.</summary>
    private void BulkEditToggle_Unchecked(object sender, RoutedEventArgs e)
    {
        _bulkEditMode = false;
        ServiceOrderList.SelectionMode = SelectionMode.Single;
        ServiceOrderList.UnselectAll();
        if (ApplyToSelectedBtn != null)
            ApplyToSelectedBtn.Visibility = Visibility.Collapsed;
    }

    /// <summary>
    /// Collect the multi-selection, open <see cref="BulkEditWindow"/>, and on confirm batch-apply
    /// the chosen fields to all selected items via the pure <see cref="Core.Services.BulkEditService"/>,
    /// persist through the existing ServiceOrder save path, and refresh the sidebar. Mirrors the
    /// established manager → SaveServiceOrderAsync → RefreshServiceOrderList pattern.
    /// </summary>
    private async void ApplyToSelected_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_serviceOrderManager?.CurrentServiceOrder == null) return;

            // Project the selected display rows to their underlying items, dropping group headers
            // (Item == null). Build a concrete list so enumeration is stable across the dialog.
            var selectedItems = ServiceOrderList.SelectedItems
                .OfType<ServiceItemDisplay>()
                .Where(d => d.Item != null)
                .Select(d => d.Item!)
                .ToList();

            if (selectedItems.Count < 2)
            {
                MessageBox.Show(
                    "Select two or more items to bulk edit.",
                    "Bulk Edit",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var window = new BulkEditWindow(selectedItems.Count) { Owner = this };

            if (window.ShowDialog() == true && window.Result != null)
            {
                Core.Services.BulkEditService.Apply(window.Result, selectedItems);
                await SaveServiceOrderAsync();
                RefreshServiceOrderList();
            }
        }
        catch (Exception ex)
        {
            ShowToolError("Bulk Edit", ex.Message);
        }
    }

    private async void RemoveServiceItem_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_serviceOrderManager?.CurrentServiceOrder == null) return;
            // C2: group-header rows are interleaved in the list, so SelectedIndex no longer maps
            // 1:1 to Items. Resolve the target item from the selected display row instead.
            if (ServiceOrderList.SelectedItem is not ServiceItemDisplay display || display.Item == null) return;

            int index = _serviceOrderManager.CurrentServiceOrder.Items.IndexOf(display.Item);
            if (index < 0) return;

            _serviceOrderManager.RemoveItem(index);
            await SaveServiceOrderAsync();
            RefreshServiceOrderList();
        }
        catch (Exception ex)
        {
            ShowToolError("Remove Service Item", ex.Message);
        }
    }

    // ─── C2 Service order groups: sidebar wiring ──────────────────
    // All handlers mirror the established manager -> SaveServiceOrderAsync -> RefreshServiceOrderList
    // pattern used by item add/edit/settings/remove.

    /// <summary>
    /// Header "§ Group" button and context "Add Group…": prompt for a name, create the group,
    /// persist, and re-render the grouped sidebar.
    /// </summary>
    private async void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_serviceOrderManager?.CurrentServiceOrder == null) return;

            var name = PromptForText("New Group", "Group name:", "");
            if (string.IsNullOrWhiteSpace(name)) return;

            _serviceOrderManager.CreateGroup(name);
            await SaveServiceOrderAsync();
            RefreshServiceOrderList();
        }
        catch (Exception ex)
        {
            ShowToolError("Add Group", ex.Message);
        }
    }

    /// <summary>
    /// Collapse/expand chevron on a group-header row: flip the group's collapsed state,
    /// persist it, and rebuild the list (a collapsed group contributes only its header row).
    /// </summary>
    private async void GroupCollapseToggle_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_serviceOrderManager == null) return;
            if (sender is not FrameworkElement fe || fe.Tag is not ServiceItemDisplay display) return;
            if (display.Group == null) return; // "Ungrouped" header is not collapsible.

            _serviceOrderManager.SetGroupCollapsed(display.Group.Id, !display.Group.IsCollapsed);
            await SaveServiceOrderAsync();
            RefreshServiceOrderList();
        }
        catch (Exception ex)
        {
            ShowToolError("Collapse Group", ex.Message);
        }
    }

    /// <summary>
    /// Rebuild dynamic parts of the sidebar context menu when it opens: populate the
    /// "Move to Group" submenu from the current groups, and enable/disable item vs group
    /// actions based on which kind of row is selected.
    /// </summary>
    private void ServiceOrderContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        var display = ServiceOrderList.SelectedItem as ServiceItemDisplay;
        var isItem = display?.Item != null;
        var isGroupHeader = display?.IsGroupHeader == true && display.Group != null;

        if (MoveToGroupMenu != null)
        {
            MoveToGroupMenu.Items.Clear();
            MoveToGroupMenu.IsEnabled = isItem;

            var order = _serviceOrderManager?.CurrentServiceOrder;
            if (order != null)
            {
                foreach (var group in order.Groups.OrderBy(g => g.Order))
                {
                    var mi = new MenuItem { Header = group.Name, Tag = group.Id };
                    mi.Click += MoveToGroup_Click;
                    MoveToGroupMenu.Items.Add(mi);
                }

                if (order.Groups.Count > 0)
                    MoveToGroupMenu.Items.Add(new Separator());

                var ungroup = new MenuItem { Header = "Ungrouped", Tag = null };
                ungroup.Click += MoveToGroup_Click;
                MoveToGroupMenu.Items.Add(ungroup);
            }
        }

        if (RenameGroupMenu != null) RenameGroupMenu.IsEnabled = isGroupHeader;
        if (DeleteGroupMenu != null) DeleteGroupMenu.IsEnabled = isGroupHeader;
    }

    /// <summary>
    /// "Move to Group ▸ &lt;name&gt;/Ungrouped": assign the selected item to the chosen group
    /// (Tag holds the group id, or null to ungroup), persist, and re-render.
    /// </summary>
    private async void MoveToGroup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_serviceOrderManager == null) return;
            if (ServiceOrderList.SelectedItem is not ServiceItemDisplay display || display.Item == null) return;
            if (sender is not MenuItem mi) return;

            var targetGroupId = mi.Tag as string; // null => ungroup
            _serviceOrderManager.AssignItemToGroup(display.Item.Id, targetGroupId);
            await SaveServiceOrderAsync();
            RefreshServiceOrderList();
        }
        catch (Exception ex)
        {
            ShowToolError("Move to Group", ex.Message);
        }
    }

    /// <summary>
    /// Context "Rename Group…" on a group-header row: prompt for a new name and persist.
    /// </summary>
    private async void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_serviceOrderManager == null) return;
            if (ServiceOrderList.SelectedItem is not ServiceItemDisplay display || display.Group == null) return;

            var name = PromptForText("Rename Group", "Group name:", display.Group.Name);
            if (string.IsNullOrWhiteSpace(name)) return;

            _serviceOrderManager.RenameGroup(display.Group.Id, name);
            await SaveServiceOrderAsync();
            RefreshServiceOrderList();
        }
        catch (Exception ex)
        {
            ShowToolError("Rename Group", ex.Message);
        }
    }

    /// <summary>
    /// Context "Delete Group" on a group-header row: remove the group (its items stay in the
    /// service order and become ungrouped), persist, and re-render.
    /// </summary>
    private async void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_serviceOrderManager == null) return;
            if (ServiceOrderList.SelectedItem is not ServiceItemDisplay display || display.Group == null) return;

            var result = MessageBox.Show(
                $"Delete group '{display.Group.Name}'? Its items stay in the service and become ungrouped.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes) return;

            _serviceOrderManager.DeleteGroup(display.Group.Id);
            await SaveServiceOrderAsync();
            RefreshServiceOrderList();
        }
        catch (Exception ex)
        {
            ShowToolError("Delete Group", ex.Message);
        }
    }

    /// <summary>
    /// Minimal modal text prompt (no resource dictionaries, inline brushes) used for group
    /// name entry. Returns the entered text, or null if cancelled/closed.
    /// </summary>
    private string? PromptForText(string title, string prompt, string initial)
    {
        var window = new Window
        {
            Title = title,
            Width = 340,
            Height = 160,
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.NoResize,
            Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x1D, 0x27))
        };

        var textBox = new TextBox
        {
            Text = initial,
            Margin = new Thickness(12, 4, 12, 4),
            FontSize = 14
        };

        var okButton = new Button
        {
            Content = "OK",
            Width = 80,
            Margin = new Thickness(0, 0, 12, 12),
            HorizontalAlignment = HorizontalAlignment.Right,
            IsDefault = true
        };

        string? result = null;
        okButton.Click += (_, _) => { result = textBox.Text; window.DialogResult = true; window.Close(); };

        var panel = new DockPanel();
        var label = new TextBlock
        {
            Text = prompt,
            Margin = new Thickness(12, 12, 12, 4),
            Foreground = Brushes.White
        };
        DockPanel.SetDock(label, Dock.Top);
        DockPanel.SetDock(okButton, Dock.Bottom);
        panel.Children.Add(label);
        panel.Children.Add(okButton);
        panel.Children.Add(textBox);

        window.Content = panel;
        textBox.Focus();
        textBox.SelectAll();
        window.ShowDialog();
        return result;
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

    private void TabLive_Click(object sender, RoutedEventArgs e) => SwitchToTab("Live");
    private void TabServices_Click(object sender, RoutedEventArgs e) => ShowServicesList();
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
        HomePanel.Visibility = Visibility.Collapsed;
        ServicesListPanel.Visibility = Visibility.Collapsed;
        ServicesPanel.Visibility = Visibility.Collapsed;
        SongsPanel.Visibility = Visibility.Collapsed;
        ScripturePanel.Visibility = Visibility.Collapsed;
        MediaPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;

        // Reset all tab button styles
        SetTabInactive(TabLive);
        SetTabInactive(TabServices);
        SetTabInactive(TabSongs);
        SetTabInactive(TabScripture);
        SetTabInactive(TabMedia);

        try
        {
            switch (tabName)
            {
                case "Live":
                    // Live tab = the presentation surface for the current service: the slide
                    // grid (ServicesPanel) alongside the always-visible SERVICE ORDER sidebar.
                    // Reuse ShowServiceEditor() rather than inventing a new panel; with no
                    // service loaded it simply shows an empty standby grid. ShowServiceEditor()
                    // sets ServicesPanel visible, marks the Live tab active, and fixes the header.
                    ShowServiceEditor();
                    break;

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
                        _songLibraryPanel.ImportChordProRequested += OnImportChordProRequested;
                        _songLibraryPanel.EditMetadataRequested += OnEditMetadataRequested;
                        _songLibraryPanel.EditSlidesRequested += OnEditSlidesRequested;
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
                        // E5: seed/persist the display-option defaults via the Presenter
                        // settings (reachable here; AppSettings/SettingsService are not).
                        _scriptureBrowserPanel.Settings = _engine?.Settings;
                        // E3: generated scripture slides reach the live output via the engine.
                        _scriptureBrowserPanel.PresentSlidesRequested += OnScripturePresentRequested;
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

    private void OnScripturePresentRequested(object? sender, List<Slide> slides)
    {
        // E3: slides were generated by the Core ScriptureSlideGenerator in the panel.
        // Load them into the engine; the existing OnSlideChanged → OutputWindow.ShowSlide
        // path then renders slide 0, and the operator navigates with GO LIVE / arrow keys
        // exactly as for standalone songs.
        _engine?.LoadScriptureSlides(slides);
    }

    private void OnImportChordProRequested(object? sender, EventArgs e)
    {
        if (_songLibraryService == null) return;

        // The dialog's own Browse button shows an OpenFileDialog (ChordPro extensions),
        // parses, and previews; on confirm it exposes the chosen path via FilePathTextBox.
        var dialog = new ChordProImportDialog { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Confirmed)
        {
            var path = dialog.FilePathTextBox.Text;
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
            {
                // Fire-and-forget the async import, mirroring OnMediaImportRequested.
                _ = ImportChordProFromFileAsync(path);
            }
        }
    }

    private async Task ImportChordProFromFileAsync(string path)
    {
        if (_songLibraryService == null) return;

        try
        {
            // Authoritative import: reuses ChordProParserService + SongManager.SaveSongAsync
            // (song.json persistence) and preserves chords/sections, unlike the dialog's
            // lossy preview. Then refresh the list so the new song shows immediately.
            await _songLibraryService.ImportChordProAsync(path);
            _songLibraryPanel?.RefreshSongs();
        }
        catch (Exception ex)
        {
            ShowToolError("Import ChordPro", ex.Message);
        }
    }

    private void OnEditMetadataRequested(object? sender, EventArgs e)
    {
        if (_songLibraryService == null || _songManager == null) return;

        var selected = _songLibraryPanel?.SelectedSong;
        if (selected == null)
        {
            ShowToolError("Edit Metadata", "Select a song first.");
            return;
        }

        // Resolve the in-memory Song the same robust way OnSongSelected does
        // (normalized path, then folder name, then title).
        var song = SongPathResolver.Resolve(_songManager.Songs, selected.Id, selected.Title);
        if (song == null)
        {
            ShowToolError("Edit Metadata",
                $"Could not resolve song (Id='{selected.Id}', Title='{selected.Title}').");
            return;
        }

        // Host the editor UserControl in a modal window (ShowDialog + read Result),
        // mirroring the ChordProImportDialog consumption pattern.
        var tags = string.Join(", ",
            song.Themes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        var window = new SongMetadataEditorWindow(
            song.Title, song.Artist, song.OriginalKey,
            (int)Math.Round(song.Bpm), song.Ccli, tags) { Owner = this };

        if (window.ShowDialog() == true && window.Result != null)
        {
            var result = window.Result;
            var item = new Core.Models.Cloud.SongLibraryItem
            {
                Id = selected.Id,
                Title = result.Title,
                Artist = result.Artist,
                Key = result.Key,
                // Editor BPM is int?; model is double. Empty BPM leaves the stored value unchanged.
                Bpm = result.Bpm ?? song.Bpm,
                CcliNumber = result.CcliNumber ?? "",
                Tags = result.Tags
            };

            // Fire-and-forget the async persist + list refresh, mirroring ImportChordProFromFileAsync.
            _ = UpdateSongMetadataAsync(item);
        }
    }

    private async Task UpdateSongMetadataAsync(Core.Models.Cloud.SongLibraryItem item)
    {
        if (_songLibraryService == null) return;

        try
        {
            // UpdateMetadataAsync mutates only metadata fields then calls SaveSongAsync,
            // which re-serializes the whole Song — sections/lyrics/chords are preserved.
            await _songLibraryService.UpdateMetadataAsync(item);
            _songLibraryPanel?.RefreshSongs();
        }
        catch (Exception ex)
        {
            ShowToolError("Edit Metadata", ex.Message);
        }
    }

    private void OnEditSlidesRequested(object? sender, EventArgs e)
    {
        if (_songLibraryService == null || _songManager == null) return;

        var selected = _songLibraryPanel?.SelectedSong;
        if (selected == null)
        {
            ShowToolError("Edit Slides", "Select a song first.");
            return;
        }

        // Resolve the in-memory Song the same robust way OnSongSelected/OnEditMetadataRequested do
        // (normalized path, then folder name, then title).
        var song = SongPathResolver.Resolve(_songManager.Songs, selected.Id, selected.Title);
        if (song == null)
        {
            ShowToolError("Edit Slides",
                $"Could not resolve song (Id='{selected.Id}', Title='{selected.Title}').");
            return;
        }

        if (song.Sections.Count == 0)
        {
            ShowToolError("Edit Slides", "This song has no slides to edit.");
            return;
        }

        // Fire-and-forget: build picker lists from the existing services, show the modal,
        // then persist. Kept off the sync path because the service lookups are async.
        _ = EditSongSlidesAsync(song, selected.Id);
    }

    private async Task EditSongSlidesAsync(Song song, string songId)
    {
        if (_songLibraryService == null) return;

        try
        {
            // Reuse the existing services for picker choices; degrade to empty lists if a
            // service is unavailable, never throw. Background images come from the media
            // library (images only); templates come from the template service.
            var imagePaths = new List<string>();
            if (_mediaLibraryService != null)
            {
                var media = await _mediaLibraryService.GetByTypeAsync(MediaLibraryType.Image);
                imagePaths.AddRange(media.Select(m => m.FilePath));
            }

            var templates = new List<(string Id, string Name)>();
            if (_templateService != null)
            {
                var all = await _templateService.GetAllAsync();
                templates.AddRange(all.Select(t => (t.Id, t.Name)));
            }

            // Host the editor UserControl in a modal window (ShowDialog + read Result),
            // mirroring the SongMetadataEditorWindow consumption pattern.
            var window = new SongSlideEditorWindow(song.Sections, imagePaths, templates) { Owner = this };

            if (window.ShowDialog() == true && window.Result != null)
            {
                // UpdateSlidesAsync copies only Text/BackgroundImage/TemplateId back onto the
                // stored sections then SaveSongAsync re-serializes the whole Song, so chords,
                // timestamps, MIDI cues, section structure, and metadata are preserved.
                await _songLibraryService.UpdateSlidesAsync(songId, window.Result);
                _songLibraryPanel?.RefreshSongs();
            }
        }
        catch (Exception ex)
        {
            ShowToolError("Edit Slides", ex.Message);
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
                _mediaLibraryPanel.AddImportedItem(new MediaItem
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

    /// <summary>
    /// Recolor the Live-tab status dot: live pink when the presentation is live on-screen,
    /// gray when idle/blacked/standby. Independent of which tab is active.
    /// </summary>
    private void UpdateLiveTabDot(bool isLive)
    {
        LiveTabDot.Fill = new SolidColorBrush(isLive ? LivePink : TextSecondary);
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
                // Construct the CCLI report service from its dependency (lazily, once) and wire the
                // dialog's data-load and CSV-export callbacks through it, so both honor the selected
                // date range and include the CCLI Number.
                _ccliReportService ??= new CcliReportService(_songUsageService);
                var reportService = _ccliReportService;

                dialog.QueryAsync = async (from, to) =>
                {
                    var entries = await reportService.GetReportAsync(from, to);
                    return entries
                        .Select(entry => new CcliReportItem
                        {
                            Title = entry.SongTitle,
                            Artist = entry.Artist,
                            CcliNumber = entry.CcliNumber,
                            TimesPlayed = entry.PlayCount,
                            LastPlayed = entry.LastPlayed
                        })
                        .ToList();
                };

                dialog.ExportAsync = (from, to, path) =>
                    reportService.ExportCsvAsync(from, to, path, _engine?.Settings?.CcliLicenseNumber);

                // Initial load honors the dialog's default From/To (last 3 months → today).
                if (dialog.FromDatePicker.SelectedDate is DateTime initialFrom
                    && dialog.ToDatePicker.SelectedDate is DateTime initialTo)
                {
                    _ = dialog.LoadForRangeAsync(initialFrom, initialTo);
                }
            }

            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowToolError("CCLI Report", ex.Message);
        }
    }

    private void ToolTemplates_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var panel = new TemplateManagerPanel();
            if (_templateService != null)
                panel.TemplateService = _templateService;
            var templatesWindow = new Window
            {
                Title = "Templates",
                Width = 720,
                Height = 560,
                MinWidth = 480,
                MinHeight = 360,
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0F1117")),
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                Content = panel
            };
            templatesWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowToolError("Templates", ex.Message);
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
