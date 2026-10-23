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
using WorshipPlayer.Presenter.Views.Slides;
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
    private ISlidePresentationService? _slidePresentationService;

    // In-memory cache of the presentations that live-linked Slides items point at, keyed by
    // SlidePresentationId. Primed asynchronously by PrimeSlidePresentationCacheAsync right before
    // the refreshes that must reflect a live edit; read synchronously by GetSlidesForItem (which
    // stays synchronous, mirroring the Song path's "resolve from an already-loaded collection").
    // A null value means the presentation was not found (deleted) — the renderer falls back safely.
    private readonly Dictionary<string, SlidePresentation?> _slidePresentationCache = new();

    // Navigation tab state
    private string _activeTab = "Services";
    private SongLibraryPanel? _songLibraryPanel;
    private SlidesLibraryPanel? _slidesLibraryPanel;
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
            _engine.AutoAdvanceCountdownChanged -= OnAutoAdvanceCountdownChanged;
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
        _engine.AutoAdvanceCountdownChanged += OnAutoAdvanceCountdownChanged;
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
        RefreshSlideSections();

        // FEAT-002: land on the Home screen. Synchronous view setup inline (Initialize is a
        // sync void and cannot await); the services scan is fired-and-forgotten but observes
        // its own exceptions via RefreshServiceListingInitialAsync.
        EnsureHomeView();
        HomePanelContent.Content = _homeView;
        HomePanel.Visibility = Visibility.Visible;
        ServicesListPanel.Visibility = Visibility.Collapsed;
        ServicesPanel.Visibility = Visibility.Collapsed;
        // Home is a non-presentation screen: hide the left sidebar + center header.
        SetSidebarVisible(false);

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
        _homeView.EditServiceRequested += OnEditServiceRequested;
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
            // Sidebar + center header follow SERVICE state, not the tab: once a service is
            // loaded they stay visible on every tab (including this Services list); hidden only
            // when nothing is loaded (fresh Home).
            SetSidebarVisible(_serviceOrderManager?.CurrentServiceOrder != null);

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
        // The slide grid IS the Live presentation surface: show the left sidebar + center header.
        SetSidebarVisible(true);

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
        ITemplateService? templateService,
        ISlidePresentationService? slidePresentationService)
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
        _slidePresentationService = slidePresentationService;

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
            UpdateLiveSectionHighlights();

            // Forward to output window
            if (e.NewSlide != null && _outputWindow != null)
            {
                _outputWindow.ShowSlide(e.NewSlide, e.Transition, e.TransitionDurationMs);
            }
        });
    }

    /// <summary>
    /// Đợt 2 T5 (R3.4/R3.5): reflect the engine's auto-advance countdown — a bar with
    /// the remaining time at the TOP of the service list frame, plus a clock marker
    /// on the counting item's row. The engine cancels the countdown the moment the
    /// current item changes (including manual navigation) or becomes ineligible, so
    /// an inactive event hides the bar and clears every marker immediately.
    /// </summary>
    private void OnAutoAdvanceCountdownChanged(object? sender, AutoAdvanceCountdownEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (!e.IsActive)
            {
                AutoAdvanceCountdownBar.Visibility = Visibility.Collapsed;
                foreach (var row in _serviceOrderItems)
                    row.IsAutoAdvanceCountingDown = false;
                return;
            }

            AutoAdvanceCountdownText.Text =
                $"Auto-advance in {AutoAdvanceCountdown.Format(e.RemainingMs)}";
            AutoAdvanceCountdownBar.Visibility = Visibility.Visible;
            foreach (var row in _serviceOrderItems)
                row.IsAutoAdvanceCountingDown = row.Item?.Id == e.ItemId;
        });
    }

    private void OnItemChanged(object? sender, ServiceItemChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            // Show the LIVE display name for the current item: for a live-linked Slides item this
            // resolves the linked presentation's current name from the primed cache (so a rename is
            // reflected), falling back to the stored Title for every other item. Mirrors the sidebar
            // row (MakeItemRow) and section header (BuildSectionHeader). The cache may not be primed
            // yet on first load; the deferred prime below re-applies this via RefreshCurrentItemTitle.
            CurrentItemTitle.Text = e.NewItem != null
                ? ServiceItemDisplayName.ResolveDisplayName(e.NewItem, ResolveLinkedPresentation(e.NewItem))
                : "";
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

            // Đợt 2 T1 (R1) + T2 (R2): hand the new current item to the output window
            // together with the live service list, so its per-item background
            // (including Inherit chains, resolved at render time) is applied under
            // the item's foreground; Theme-kind items are a no-op visually (the
            // theme background is re-applied as before).
            _outputWindow?.SetCurrentItem(e.NewItem, _engine?.CurrentServiceOrder?.Items);

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

            RefreshSlideSections();
            UpdatePreviews();
            UpdateSlideCounter();

            // Opening a service ends in GoToItem(0) -> ItemChanged (here). Prime the linked-
            // presentation cache off the UI thread's pump (non-blocking, keeps this handler
            // synchronous) then rebuild the sections so any live-linked Slides items show their
            // CURRENT slides on the next service open / tab return. Failures are swallowed inside
            // the prime helper.
            _ = Dispatcher.InvokeAsync(async () =>
            {
                await PrimeSlidePresentationCacheAsync();

                // The cache is now populated, so a live-linked Slides item's renamed name can
                // resolve. Rebuild the SIDEBAR (RefreshServiceOrderList) and the center grid
                // (RefreshSlideSections), and re-apply the current-item header — on first load /
                // navigation the sidebar + header were built before the cache was primed and would
                // otherwise keep showing the stale stored Title.
                RefreshServiceOrderList();
                RefreshSlideSections();
                var headerItem = _engine?.CurrentItem;
                if (headerItem != null)
                    CurrentItemTitle.Text = ServiceItemDisplayName.ResolveDisplayName(
                        headerItem, ResolveLinkedPresentation(headerItem));

                // This async rebuild clears + rebuilds SlideSectionsPanel and resets scroll to
                // the top, clobbering any earlier selection scroll (and for Slides items the
                // section height only settles after the cache is primed here). Re-issue the
                // scroll to the engine's CURRENT item after this LAST rebuild, deferred to
                // DispatcherPriority.Loaded so it runs post-layout against the final section
                // height. Keying off _engine.CurrentItem (the live ServiceItem, same instance
                // used as the _sectionByItem key) means the latest selection wins on rapid
                // navigation. Type-agnostic: reuses the shared ScrollToSection/_sectionByItem.
                var current = _engine?.CurrentItem;
                if (current != null)
                {
                    Dispatcher.InvokeAsync(() => ScrollToSection(current), System.Windows.Threading.DispatcherPriority.Loaded);
                }
            });
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

    /// <summary>
    /// Show/hide the left SERVICE ORDER sidebar AND the center current-item header together.
    /// Visible = the presentation (Live) surface; hidden = Home/Services/Songs/Scripture/Media.
    /// Collapses the Border AND zeroes SidebarColumn.Width so no invisible strip captures clicks.
    /// Honors the user's existing collapse choice (_isSidebarCollapsed) when re-showing; does not
    /// mutate _isSidebarCollapsed, so the narrow/wide preference survives a Home round-trip.
    /// </summary>
    private void SetSidebarVisible(bool visible)
    {
        if (visible)
        {
            ServiceOrderSidebar.Visibility = Visibility.Visible;
            SidebarColumn.Width = new GridLength(_isSidebarCollapsed ? 48 : 200);
            CurrentItemHeader.Visibility = Visibility.Visible;
        }
        else
        {
            ServiceOrderSidebar.Visibility = Visibility.Collapsed;
            SidebarColumn.Width = new GridLength(0);
            CurrentItemHeader.Visibility = Visibility.Collapsed;
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

        // Reapply selection/live highlights on the live section for the new edit state.
        UpdateLiveSectionHighlights();
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
        // Re-render thumbnails across ALL sections at the new zoom size
        if (_engine != null)
        {
            RefreshSlideSections();
        }
    }

    // --- Keyboard Shortcuts ---

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        // Don't intercept if a text-editable input has focus
        if (e.OriginalSource is TextBoxBase || e.OriginalSource is ComboBox) return;

        // Slide-label hotkeys (đợt 2 T8 / R5.4): a plain letter key bound to a label jumps
        // to that label's first section in the live song. When it doesn't match, every
        // built-in key below keeps its exact existing behaviour.
        if (Keyboard.Modifiers == ModifierKeys.None && TryHandleSlideLabelHotkey(e.Key))
        {
            e.Handled = true;
            return;
        }

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
    private sealed class ServiceItemDisplay : System.ComponentModel.INotifyPropertyChanged
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

        // Đợt 2 T2 (R2): items in Inherit background mode show a small marker next to
        // their name so the operator can see the inheritance at a glance.
        public Visibility InheritBadgeVisibility => Item?.BackgroundKind == ItemBackgroundKind.Inherit
            ? Visibility.Visible : Visibility.Collapsed;

        // Đợt 2 T5 (R3.4/R3.5): live clock marker shown only while THIS item's
        // auto-advance countdown is running. Set from the engine's countdown events
        // (and at row creation, from the engine's current countdown item), so it
        // needs change notification unlike the static badges above.
        private bool _isAutoAdvanceCountingDown;
        public bool IsAutoAdvanceCountingDown
        {
            get => _isAutoAdvanceCountingDown;
            set
            {
                if (_isAutoAdvanceCountingDown == value) return;
                _isAutoAdvanceCountingDown = value;
                PropertyChanged?.Invoke(this,
                    new System.ComponentModel.PropertyChangedEventArgs(nameof(CountdownBadgeVisibility)));
            }
        }

        public Visibility CountdownBadgeVisibility => _isAutoAdvanceCountingDown
            ? Visibility.Visible : Visibility.Collapsed;

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
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
    /// Build an item (non-header) display row for the sidebar. Instance method (not static) so it
    /// can read <see cref="_slidePresentationCache"/> to DISPLAY a live-linked Slides item's CURRENT
    /// presentation name (resolved at render time via <see cref="ServiceItemDisplayName"/>) — a
    /// rename of the presentation updates this row's label without rewriting the stored Title. The
    /// icon mapping is name-independent, so the <see cref="BuildSectionHeader"/> caller that only
    /// reads <see cref="ServiceItemDisplay.Icon"/> is unaffected.
    /// </summary>
    private ServiceItemDisplay MakeItemRow(ServiceItem item)
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
            Title = ServiceItemDisplayName.ResolveDisplayName(item, ResolveLinkedPresentation(item)),
            Item = item,
            // Đợt 2 T5 (R3.4): a rebuilt list must keep the clock marker on the item
            // whose countdown is currently running.
            IsAutoAdvanceCountingDown =
                _engine?.AutoAdvanceCountdownItemId != null &&
                item.Id == _engine.AutoAdvanceCountdownItemId
        };
    }

    /// <summary>
    /// Look up the already-primed library presentation for a live-linked Slides item from
    /// <see cref="_slidePresentationCache"/>. Returns null for non-Slides items, Slides items with
    /// no link id, and ids that are missing/deleted or not yet primed — in every case the display
    /// name falls back safely to the stored Title. Synchronous: it only reads the cache that
    /// <see cref="PrimeSlidePresentationCacheAsync"/> populates before the refreshes, so no UI-thread
    /// blocking/async is introduced (same pattern <see cref="GetSlidesForItem"/> relies on).
    /// </summary>
    private SlidePresentation? ResolveLinkedPresentation(ServiceItem item)
    {
        if (item.Type != ServiceItemType.Slides || string.IsNullOrEmpty(item.SlidePresentationId))
            return null;

        return _slidePresentationCache.GetValueOrDefault(item.SlidePresentationId);
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
    /// previews, slide counter, and live-slide highlight. In the all-sections center pane the
    /// selection no longer replaces content — all sections stay rendered and we scroll the right
    /// pane to the matching section via ScrollToSection.
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
            // Keep engine/live state + the go-live/transport flow intact: GoToItem fires
            // ItemChanged -> RefreshSlideSections (full rebuild) + UpdatePreviews, so the
            // section map is fresh before we scroll.
            _serviceOrderManager?.GoToItem(index);

            // All-sections pane: selection no longer REPLACES the center content. Every
            // section stays rendered; we just scroll the right pane to the matching section.
            // The synchronous rebuild via ItemChanged has re-registered the section element,
            // but WPF has not yet run a Measure/Arrange pass on the freshly built elements, so
            // a BringIntoView issued now would target an un-arranged element and no-op / snap
            // to top. Defer the scroll to DispatcherPriority.Loaded so it runs after layout.
            // Capture the selected item in a local so a rapid second selection does not scroll
            // to a stale item. Keyed by the ServiceItem (type-agnostic) via _sectionByItem.
            var target = display.Item;
            Dispatcher.InvokeAsync(() => ScrollToSection(target), System.Windows.Threading.DispatcherPriority.Loaded);
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

    /// <summary>
    /// FEAT-003: route a Slides service item to the dedicated <see cref="SlideServiceItemEditorWindow"/>.
    /// Returns false (so the caller falls back to the generic AddServiceItemDialog) when the item is
    /// not a Slides item, when no service order is loaded, or when any required service is unavailable.
    /// On success it opens the editor modally, runs the remove/save/refresh flow, and returns true.
    /// </summary>
    private bool TryOpenSlideServiceItemEditor(ServiceItem item)
    {
        if (item.Type != ServiceItemType.Slides) return false;
        if (_serviceOrderManager?.CurrentServiceOrder == null) return false;
        if (_slidePresentationService == null || _mediaLibraryService == null || _templateService == null) return false;

        var win = new SlideServiceItemEditorWindow(
            item,
            _serviceOrderManager.CurrentServiceOrder.Items,
            _slidePresentationService,
            _mediaLibraryService,
            _templateService)
        {
            Owner = this
        };

        bool removed = false;
        win.RemoveRequested += (_, __) =>
        {
            removed = true;
            win.DialogResult = false;
            win.Close();
        };

        // When the user edits slide CONTENT via the inner SlideEditorWindow, re-prime the host cache
        // and refresh the sidebar/center grid through the EXISTING flow so added slides show now,
        // even if the user later Closes (discards) the service-item editor.
        win.LinkedPresentationSlidesEdited += (_, __) =>
        {
            _ = Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    await PrimeSlidePresentationCacheAsync();
                    RefreshServiceOrderList();
                    RefreshSlideSections();
                }
                catch (Exception ex)
                {
                    ShowToolError("Edit Service Item", ex.Message);
                }
            });
        };

        bool saved = win.ShowDialog() == true;

        // removed and saved are mutually exclusive. Run the persistence/refresh flow on the
        // dispatcher so the awaited save/prime happens after ShowDialog returns.
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (removed)
                {
                    int index = _serviceOrderManager.CurrentServiceOrder.Items.IndexOf(item);
                    if (index >= 0) _serviceOrderManager.RemoveItem(index);
                }
                else if (!saved)
                {
                    return; // discarded: nothing was copied back.
                }
                // On save the window already copied the edited fields back onto the live item.

                await SaveServiceOrderAsync();
                await PrimeSlidePresentationCacheAsync();
                RefreshServiceOrderList();
                RefreshSlideSections();
            }
            catch (Exception ex)
            {
                ShowToolError("Edit Service Item", ex.Message);
            }
        });

        return true;
    }

    private void EditSelectedServiceItem()
    {
        try
        {
            if (_serviceOrderManager == null) return;
            if (ServiceOrderList.SelectedItem is not ServiceItemDisplay display) return;
            if (display.Item == null) return; // C2: ignore group-header rows.

            // FEAT-003: Slides items open the dedicated editor; every other type falls through
            // to the generic AddServiceItemDialog below.
            if (TryOpenSlideServiceItemEditor(display.Item)) return;

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

    // ─── Service overflow (⋮) menu ────────────────────────────────
    // The header ⋮ button opens its own ContextMenu (same pattern as ServicesListView.Overflow_Click
    // and the TabTools button). Menu items reuse the existing service handlers where possible and add
    // Print / Open-in-Planning / Close actions.

    /// <summary>
    /// Open the service overflow menu anchored under the ⋮ button (mirrors
    /// ServicesListView.Overflow_Click).
    /// </summary>
    private void ServiceMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            btn.ContextMenu.IsOpen = true;
        }
    }

    /// <summary>
    /// Enable/disable the service-dependent menu items when the overflow menu opens. Creating a new
    /// service item is always allowed (opens new-service mode); "Open in Planning" is a placeholder.
    /// </summary>
    private void ServiceOverflowMenu_Opened(object sender, RoutedEventArgs e)
    {
        bool hasService = _serviceOrderManager?.CurrentServiceOrder != null;
        MenuCreateItem.IsEnabled = true;
        MenuCreateGroup.IsEnabled = hasService;
        MenuEditSettings.IsEnabled = hasService;
        MenuPrintService.IsEnabled = hasService;
        MenuCloseService.IsEnabled = hasService;
        MenuOpenInPlanning.IsEnabled = true;
    }

    /// <summary>
    /// "Print service": open <see cref="PrintExportDialog"/> for the current service. Guarded so a
    /// missing service is a no-op (the menu item is also disabled in that case).
    /// </summary>
    private void OnPrintService(object sender, RoutedEventArgs e)
    {
        try
        {
            var order = _serviceOrderManager?.CurrentServiceOrder;
            if (order == null) return;

            var entries = order.Items
                .Select(i => new WorshipPlayer.Presenter.Views.ServiceBuilder.ServiceOrderEntry
                {
                    Title = i.Title,
                    Type = i.Type.ToString(),
                    Lyrics = null
                })
                .ToList();

            var dlg = new WorshipPlayer.Presenter.Views.ServiceBuilder.PrintExportDialog
            {
                Owner = this,
                ExportService = _exportService
            };
            dlg.LoadServiceOrder(order.Name, entries);
            dlg.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowToolError("Print Service", ex.Message);
        }
    }

    /// <summary>
    /// "Open service in Planning": placeholder for a future cloud feature. Shows a gentle notice and
    /// does nothing else.
    /// </summary>
    private void OnOpenInPlanning(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Service Planning is a future cloud feature and is not available yet.",
            "Open service in Planning", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    /// <summary>
    /// "Close service": unload the current service (in-memory only, no file touched) and return to
    /// the Home screen, mirroring the Initialize() Home-restore sequence. Guarded and crash-safe.
    /// </summary>
    private void OnCloseService(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_serviceOrderManager?.CurrentServiceOrder == null) return;

            var confirm = MessageBox.Show(this,
                "Close the current service and return to Home?",
                "Close Service", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            // Unload the in-memory order (raises ItemChanged so the engine clears its slides).
            _serviceOrderManager.CloseCurrent();
            _currentServicePath = null;

            // Home-restore sequence (mirrors Initialize()).
            EnsureHomeView();
            HomePanelContent.Content = _homeView;
            HomePanel.Visibility = Visibility.Visible;
            ServicesListPanel.Visibility = Visibility.Collapsed;
            ServicesPanel.Visibility = Visibility.Collapsed;
            SetSidebarVisible(false);

            // Drop the now-empty service state from the UI (all null-safe).
            RefreshServiceOrderList();
            RefreshSlideSections();
            UpdatePreviews();
            UpdateSlideCounter();
            _ = RefreshServiceListingInitialAsync();
        }
        catch (Exception ex)
        {
            ShowToolError("Close Service", ex.Message);
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

        // Đợt 2 T6: group reorder entries — only on a real group header, and disabled at
        // the ends of the group sequence (first group cannot move up, last cannot move down).
        if (MoveGroupUpMenu != null || MoveGroupDownMenu != null)
        {
            var groupIndex = -1;
            var groupCount = 0;
            var order2 = _serviceOrderManager?.CurrentServiceOrder;
            if (isGroupHeader && order2 != null)
            {
                var sorted = order2.Groups.OrderBy(g => g.Order).ToList();
                groupCount = sorted.Count;
                groupIndex = sorted.FindIndex(g => g.Id == display!.Group!.Id);
            }

            if (MoveGroupUpMenu != null) MoveGroupUpMenu.IsEnabled = isGroupHeader && groupIndex > 0;
            if (MoveGroupDownMenu != null) MoveGroupDownMenu.IsEnabled = isGroupHeader && groupIndex >= 0 && groupIndex < groupCount - 1;
        }
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
    /// Context "Move Group Up" on a group-header row (đợt 2 T6): swap the group with the
    /// one above it, persist, and re-render.
    /// </summary>
    private async void MoveGroupUp_Click(object sender, RoutedEventArgs e)
        => await MoveSelectedGroupAsync(-1);

    /// <summary>
    /// Context "Move Group Down" on a group-header row (đợt 2 T6): swap the group with the
    /// one below it, persist, and re-render.
    /// </summary>
    private async void MoveGroupDown_Click(object sender, RoutedEventArgs e)
        => await MoveSelectedGroupAsync(+1);

    private async Task MoveSelectedGroupAsync(int delta)
    {
        try
        {
            if (_serviceOrderManager?.CurrentServiceOrder == null) return;
            if (ServiceOrderList.SelectedItem is not ServiceItemDisplay display || display.Group == null) return;

            var sorted = _serviceOrderManager.CurrentServiceOrder.Groups.OrderBy(g => g.Order).ToList();
            var index = sorted.FindIndex(g => g.Id == display.Group.Id);
            if (index < 0) return;

            _serviceOrderManager.MoveGroup(display.Group.Id, index + delta);
            await SaveServiceOrderAsync();
            RefreshServiceOrderList();
        }
        catch (Exception ex)
        {
            ShowToolError("Move Group", ex.Message);
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

    // ─── All-sections center pane ─────────────────────────────────────────────
    // ProPresenter-style: the center pane stacks EVERY service item as its own section
    // (header + grid of that item's slide thumbnails), instead of showing only the one
    // selected item's slides. Left-sidebar selection scrolls the right pane to the matching
    // section (ScrollToSection) rather than replacing content.
    //
    // ServiceItem -> its section root container, so ScrollToSection can BringIntoView the
    // whole section and UpdateLiveSectionHighlights can find the LIVE item's thumbnail grid.
    // Reference equality: the ServiceItem instances come straight from CurrentServiceOrder.Items
    // (the same instances the sidebar rows hold via ServiceItemDisplay.Item).
    private readonly Dictionary<ServiceItem, FrameworkElement> _sectionByItem = new();

    /// <summary>
    /// Rebuild the center pane as one SECTION per service item (ProPresenter-style stack).
    /// Iterates <c>CurrentServiceOrder.Items</c> in order; for each item builds a header
    /// (title + Edit affordance) and a WrapPanel of that item's slide thumbnails. The LIVE
    /// item (index == <c>_engine.CurrentItemIndex</c>) paints its live slide RED; all other
    /// sections render neutral thumbnails. Per-item try/catch so one bad item can't abort the
    /// whole build. Eager/synchronous: a service is a handful of items and each thumbnail is the
    /// same cost as the proven single-item path, so no async/long sync loop is introduced on the
    /// UI thread. The <see cref="_sectionByItem"/> map is the hook if lazy per-section fill is
    /// ever needed for very large services.
    /// </summary>
    private void RefreshSlideSections()
    {
        SlideSectionsPanel.Children.Clear();
        _sectionByItem.Clear();

        var order = _engine?.CurrentServiceOrder;
        if (_engine == null || order == null || order.Items.Count == 0)
        {
            // Nothing loaded — show the empty-state prompt.
            EmptyStateText.Visibility = Visibility.Visible;
            return;
        }

        EmptyStateText.Visibility = Visibility.Collapsed;

        // Zoom size from slider (default 140 if slider not yet loaded). Shared across ALL
        // sections so the zoom slider resizes every thumbnail.
        double thumbWidth = 140;
        if (ZoomSlider != null)
        {
            thumbWidth = ZoomSlider.Value;
        }

        // Reset edit-mode tracking (mirrors the old single-item reset). Live tracking follows
        // the engine's current slide.
        _lastActiveSlideIndex = _engine.CurrentSlideIndex;
        if (!_isEditMode)
        {
            _selectedSlideIndex = -1;
        }

        int liveItemIndex = _engine.CurrentItemIndex;

        for (int itemIndex = 0; itemIndex < order.Items.Count; itemIndex++)
        {
            var item = order.Items[itemIndex];
            int capturedItemIndex = itemIndex;

            // Section root: header + thumbnail grid. Registered in the section map so
            // ScrollToSection/BringIntoView and the live-highlight lookup can find it.
            var sectionRoot = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };

            try
            {
                sectionRoot.Children.Add(BuildSectionHeader(item));

                var grid = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };

                // The item's song (for slide-label resolution, đợt 2 T8 / R5.4): the same
                // resolver GetSlidesForItem uses. Null when the song isn't in the library —
                // label resolution then falls back to the section-name rule.
                var itemSong = _songManager != null
                    ? SongPathResolver.Resolve(_songManager.Songs, item.SongFolder, item.Title)
                    : null;

                var slides = GetSlidesForItem(item);
                if (slides.Count == 0)
                {
                    // No slides to render. For a Song this almost always means the song has
                    // no lyrics yet (its sections carry no text) rather than a real load
                    // failure, so say so clearly instead of the misleading "could not load".
                    string note;
                    if (item.Type == ServiceItemType.Song)
                    {
                        note = "This song has no lyrics yet. Add lyrics in the song editor to see slides here.";
                    }
                    else if (item.Type == ServiceItemType.Slides && !string.IsNullOrEmpty(item.SlidePresentationId))
                    {
                        // Linked Slides item with zero slides: point the user at where to add them.
                        note = "This presentation has no slides yet. Open Edit and click + Add slides to create one.";
                    }
                    else
                    {
                        note = "No slides to show for this item.";
                    }
                    grid.Children.Add(new TextBlock
                    {
                        Text = note,
                        FontSize = 11,
                        Foreground = new SolidColorBrush(TextSecondary),
                        Margin = new Thickness(6, 2, 6, 6),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                else
                {
                    bool isLiveSection = (capturedItemIndex == liveItemIndex);
                    for (int slideIndex = 0; slideIndex < slides.Count; slideIndex++)
                    {
                        int capturedSlideIndex = slideIndex;
                        bool isActive = isLiveSection && slideIndex == _engine.CurrentSlideIndex;
                        bool isSelected = isLiveSection && _isEditMode && slideIndex == _selectedSlideIndex;

                        var thumb = BuildSlideThumbnail(
                            slides[slideIndex], slideIndex + 1, isActive, isSelected, thumbWidth, itemSong,
                            idx =>
                            {
                                if (_isEditMode && isLiveSection)
                                {
                                    // Edit mode on the live section: select (highlight) only.
                                    _selectedSlideIndex = idx;
                                    UpdateLiveSectionHighlights();
                                }
                                else
                                {
                                    // Performance: make this item live, then fire the slide.
                                    _serviceOrderManager?.GoToItem(capturedItemIndex);
                                    _engine?.GoToSlide(idx);
                                }
                            });

                        // Đợt 2 T10 / R6.2: right-click a thumbnail to edit THIS item's
                        // own slide order (move / duplicate / reset). Live-linked Slides
                        // items are excluded — they follow their presentation.
                        if (SupportsItemSlideOrder(item))
                        {
                            thumb.ContextMenu = BuildSlideOrderContextMenu(item, slides, capturedSlideIndex);
                        }

                        grid.Children.Add(thumb);
                    }
                }

                sectionRoot.Children.Add(grid);
            }
            catch (Exception ex)
            {
                // One bad item must not abort the whole build.
                System.Diagnostics.Debug.WriteLine(
                    $"RefreshSlideSections: failed to build section for '{item.Title}': {ex.Message}");
                sectionRoot.Children.Add(new TextBlock
                {
                    Text = "This item could not be loaded.",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(TextSecondary),
                    Margin = new Thickness(6, 2, 6, 6)
                });
            }

            _sectionByItem[item] = sectionRoot;
            SlideSectionsPanel.Children.Add(sectionRoot);
        }
    }

    /// <summary>
    /// Build a section header row: item title + icon on the left, an "Edit" affordance on the
    /// right (opens the existing per-item edit dialog), and a non-functional "ORDER [Original]"
    /// chip. Inline brushes only (OperatorWindow is StaticResource-free).
    /// </summary>
    private UIElement BuildSectionHeader(ServiceItem item)
    {
        var header = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1A1D27")),
            BorderBrush = new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Padding = new Thickness(8, 6, 8, 6),
            CornerRadius = new CornerRadius(4)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        // Left: icon + title (reuse the sidebar icon mapping for consistency).
        var titleStack = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        titleStack.Children.Add(new TextBlock
        {
            Text = MakeItemRow(item).Icon,
            FontSize = 13,
            Foreground = new SolidColorBrush(AccentCyan),
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });
        // DISPLAY the live-linked Slides presentation's CURRENT name (resolved from the primed
        // cache at render time), falling back to the stored Title for every other item and to
        // "(untitled)" when the resolved name is blank. Keeps stored data unchanged.
        var displayName = ServiceItemDisplayName.ResolveDisplayName(item, ResolveLinkedPresentation(item));
        titleStack.Children.Add(new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(displayName) ? "(untitled)" : displayName,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F2F5")),
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        });
        Grid.SetColumn(titleStack, 0);
        grid.Children.Add(titleStack);

        // Middle: "ORDER" chip (matches reference). Đợt 2 T10 / R6.2: the chip now
        // tells the truth — [Custom] when this item carries its own slide order
        // (ServiceItem.SlideOrder), [Original] when it follows the song/default order.
        var orderChip = new TextBlock
        {
            Text = ServiceItemSlideOrder.HasCustomOrder(item) ? "ORDER [Custom]" : "ORDER [Original]",
            FontSize = 9,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#8B92A5")),
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        };
        Grid.SetColumn(orderChip, 1);
        grid.Children.Add(orderChip);

        // Right: Edit affordance -> reuse the existing per-item edit dialog for THIS item.
        var editBtn = new Button
        {
            Content = "Edit",
            FontSize = 10,
            Padding = new Thickness(8, 3, 8, 3),
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#252A36")),
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F0F2F5")),
            BorderBrush = new SolidColorBrush(Colors.Transparent),
            BorderThickness = new Thickness(0),
            Cursor = Cursors.Hand,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        editBtn.Click += (s, ev) => EditServiceItemDirect(item);
        Grid.SetColumn(editBtn, 2);
        grid.Children.Add(editBtn);

        header.Child = grid;
        return header;
    }

    /// <summary>
    /// Open the existing per-item edit dialog for a specific item (the section header "Edit"
    /// affordance). Mirrors <see cref="EditSelectedServiceItem"/> but targets the given item
    /// directly instead of the sidebar selection.
    /// </summary>
    private void EditServiceItemDirect(ServiceItem item)
    {
        try
        {
            if (_serviceOrderManager == null) return;

            // FEAT-003: Slides items open the dedicated editor; every other type falls through
            // to the generic AddServiceItemDialog below.
            if (TryOpenSlideServiceItemEditor(item)) return;

            var dialog = new AddServiceItemDialog
            {
                Owner = this,
                ServiceOrderManager = _serviceOrderManager,
                SongLibrary = _songManager?.Songs ?? (IReadOnlyList<Song>)Array.Empty<Song>(),
                ItemToEdit = item
            };
            dialog.ItemEdited += async (s, edited) =>
            {
                await SaveServiceOrderAsync();
                RefreshServiceOrderList();
                RefreshSlideSections();
            };
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            ShowToolError("Edit Service Item", ex.Message);
        }
    }

    /// <summary>
    /// Resolve the slide list for a service item WITHOUT mutating engine/live state.
    /// For Song items: resolve the linked library song (SongPathResolver) and generate its
    /// lyric slides via the pure <see cref="PresenterEngine.GenerateSlidesForSong"/> — the SAME
    /// generator the live path feeds through LoadStandaloneSong, so sections match live rendering.
    /// For every other type: return the pre-baked <c>item.Slides</c>. Returns an empty list on an
    /// unresolved song or missing engine/song manager (caller renders a "could not load" note).
    /// Generation is on-demand and never persisted.
    /// </summary>
    private List<Slide> GetSlidesForItem(ServiceItem item)
    {
        if (item == null) return new List<Slide>();

        if (item.Type == ServiceItemType.Song)
        {
            List<Slide> songSlides;

            // Pre-baked slides win if present (F2/F3: item slides are the source of truth).
            if (item.Slides != null && item.Slides.Count > 0)
            {
                songSlides = item.Slides;
            }
            else if (_engine == null || _songManager == null)
            {
                return new List<Slide>();
            }
            else
            {
                var song = SongPathResolver.Resolve(_songManager.Songs, item.SongFolder, item.Title);
                if (song == null)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"GetSlidesForItem: could not resolve Song item " +
                        $"(SongFolder='{item.SongFolder}', Title='{item.Title}').");
                    return new List<Slide>();
                }

                songSlides = _engine.GenerateSlidesForSong(song);
            }

            // Đợt 2 T10 / R6.2: the item's own slide order layers on top of whatever
            // the pipeline resolved (baked slides, or T9 custom order via generation);
            // a null order leaves the sequence untouched.
            return ServiceItemSlideOrder.Apply(songSlides, item.SlideOrder);
        }

        // Live-linked Slides item: resolve the presentation's CURRENT slides on demand from the
        // primed cache so editing the presentation shows live (mirrors the Song link path above).
        if (item.Type == ServiceItemType.Slides && !string.IsNullOrEmpty(item.SlidePresentationId))
        {
            if (_slidePresentationCache.TryGetValue(item.SlidePresentationId, out var presentation)
                && presentation != null)
            {
                return SlidePresentationMapper.ToSlides(presentation);
            }

            // Not primed yet or the presentation was deleted/not found: fall through to item.Slides
            // (empty for a pure link), so the caller's slides.Count == 0 path renders the safe
            // "No slides to show for this item." note. No crash.
            System.Diagnostics.Debug.WriteLine(
                $"GetSlidesForItem: linked presentation '{item.SlidePresentationId}' not available " +
                $"(deleted or cache not primed) for item '{item.Title}'.");
        }

        // Backward compat: a Slides item with no SlidePresentationId (legacy snapshot) renders its
        // stored item.Slides. (The live-link branch above deliberately returns WITHOUT the
        // per-item order — T10 scope exclusion: linked items follow their presentation.)
        // Đợt 2 T10 / R6.2: every other item type honors its own stored slide order here.
        return ServiceItemSlideOrder.Apply(item.Slides, item.SlideOrder);
    }

    /// <summary>
    /// Đợt 2 T10 / R6.2: items that support a per-item slide order. Live-linked Slides
    /// items are excluded by spec — they follow their presentation, never an item order.
    /// </summary>
    private static bool SupportsItemSlideOrder(ServiceItem item) =>
        !(item.Type == ServiceItemType.Slides && !string.IsNullOrEmpty(item.SlidePresentationId));

    private enum ItemSlideOrderEdit { MoveLeft, MoveRight, Duplicate, Reset }

    /// <summary>
    /// Build the right-click menu for one slide thumbnail: move the slide within THIS
    /// item's own order, duplicate it (the copy gets a new id and a place in the item's
    /// order), or reset the item's order back to the song/default sequence. The menu
    /// captures the item's currently displayed (already order-applied) slide list.
    /// </summary>
    private ContextMenu BuildSlideOrderContextMenu(ServiceItem item, List<Slide> displayedSlides, int slideIndex)
    {
        var menu = new ContextMenu();

        var moveLeft = new MenuItem { Header = "Move Slide Left", IsEnabled = slideIndex > 0 };
        moveLeft.Click += async (s, ev) =>
            await EditItemSlideOrderAsync(item, displayedSlides, slideIndex, ItemSlideOrderEdit.MoveLeft);
        menu.Items.Add(moveLeft);

        var moveRight = new MenuItem
        {
            Header = "Move Slide Right",
            IsEnabled = slideIndex >= 0 && slideIndex < displayedSlides.Count - 1
        };
        moveRight.Click += async (s, ev) =>
            await EditItemSlideOrderAsync(item, displayedSlides, slideIndex, ItemSlideOrderEdit.MoveRight);
        menu.Items.Add(moveRight);

        var duplicate = new MenuItem { Header = "Duplicate Slide" };
        duplicate.Click += async (s, ev) =>
            await EditItemSlideOrderAsync(item, displayedSlides, slideIndex, ItemSlideOrderEdit.Duplicate);
        menu.Items.Add(duplicate);

        menu.Items.Add(new Separator());

        var reset = new MenuItem
        {
            Header = "Reset Item Slide Order",
            IsEnabled = ServiceItemSlideOrder.HasCustomOrder(item)
        };
        reset.Click += async (s, ev) =>
            await EditItemSlideOrderAsync(item, displayedSlides, slideIndex, ItemSlideOrderEdit.Reset);
        menu.Items.Add(reset);

        return menu;
    }

    /// <summary>
    /// Apply one per-item slide-order edit. Every action writes ONLY to the item — its
    /// baked <see cref="ServiceItem.Slides"/> and its <see cref="ServiceItem.SlideOrder"/>
    /// — then saves the service order. The linked Song and sibling items are never
    /// touched (R6.2 isolation). The pure reorder math lives in
    /// <see cref="ServiceItemSlideOrder"/> (Core) so it is unit-tested.
    /// </summary>
    private async Task EditItemSlideOrderAsync(
        ServiceItem item, List<Slide> displayedSlides, int slideIndex, ItemSlideOrderEdit edit)
    {
        try
        {
            if (edit == ItemSlideOrderEdit.Reset)
            {
                // Back to the song/default order: drop the override only. The item's
                // baked slide list stays as it is (F2/F3: item slides are the truth).
                item.SlideOrder = null;
            }
            else if (edit == ItemSlideOrderEdit.Duplicate)
            {
                if (slideIndex < 0 || slideIndex >= displayedSlides.Count) return;
                var (newSlides, newOrder) = ServiceItemSlideOrder.Duplicate(
                    displayedSlides, item.SlideOrder, displayedSlides[slideIndex].Id);
                item.Slides = newSlides;
                item.SlideOrder = newOrder;
            }
            else
            {
                if (slideIndex < 0 || slideIndex >= displayedSlides.Count) return;
                int delta = edit == ItemSlideOrderEdit.MoveLeft ? -1 : 1;
                var newOrder = ServiceItemSlideOrder.Move(
                    ServiceItemSlideOrder.CaptureOrder(displayedSlides), slideIndex, delta);
                // Bake the reordered display sequence as the item's slide list so the
                // stored order and the baked slides can never drift apart.
                item.Slides = ServiceItemSlideOrder.Apply(displayedSlides, newOrder);
                item.SlideOrder = newOrder;
            }

            await SaveServiceOrderAsync();

            // If this item is live, reload the engine's slides in the new order
            // (GoToItem re-fires ItemChanged even for the same index).
            var order = _serviceOrderManager?.CurrentServiceOrder;
            int itemIndex = order?.Items.IndexOf(item) ?? -1;
            if (itemIndex >= 0 && _engine != null && itemIndex == _engine.CurrentItemIndex)
            {
                _serviceOrderManager?.GoToItem(itemIndex);
            }

            RefreshSlideSections();
        }
        catch (Exception ex)
        {
            ShowToolError("Edit Slide Order", ex.Message);
        }
    }

    /// <summary>
    /// Build a single slide thumbnail reusing the exact per-slide visual: slide background +
    /// scrim, center lyric text, a section label chip (tagged "labelChip"), and a number badge.
    /// RED (OwcLive) frame/chip when <paramref name="isActive"/> (live), cyan when
    /// <paramref name="isSelected"/> (edit-mode selection), neutral otherwise. The click handler
    /// invokes <paramref name="onClick"/> with the zero-based slide index.
    /// </summary>
    private Border BuildSlideThumbnail(Slide slide, int slideNumber, bool isActive, bool isSelected,
        double thumbWidth, Song? labelSong, Action<int> onClick)
    {
        double thumbHeight = thumbWidth * 0.6;

        // Outer border = the thumbnail frame. The CURRENTLY LIVE slide gets a RED (OwcLive)
        // border; the edit-mode selected slide a cyan one; otherwise the neutral panel border.
        var border = new Border
        {
            Width = thumbWidth,
            Height = thumbHeight,
            Background = new SolidColorBrush(PanelBg),
            BorderBrush = isActive
                ? new SolidColorBrush(LivePink)
                : isSelected
                    ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0088AA"))
                    : new SolidColorBrush(BorderColor),
            BorderThickness = new Thickness(isActive ? 2 : isSelected ? 2 : 1),
            Margin = new Thickness(4),
            CornerRadius = new CornerRadius(6),
            Cursor = Cursors.Hand
        };

        // Inner grid clipped to the rounded frame so the slide BACKGROUND fills the tile.
        var grid = new Grid
        {
            ClipToBounds = true
        };

        // Calculate font sizes proportional to thumbnail size (WYSIWYG scaling)
        double scaleFactor = thumbWidth / 140.0; // 140 is the default/base size
        double sectionFontSize = Math.Max(8, 8 * scaleFactor);
        double mainFontSize = Math.Max(9, 11 * scaleFactor);
        double numFontSize = Math.Max(8, 8 * scaleFactor);
        int maxTextLength = thumbWidth > 300 ? 200 : thumbWidth > 200 ? 80 : 40;

        // ── Layer 0: slide BACKGROUND (image / solid / gradient default) ──
        // Mirrors the live OutputWindow background so the grid previews real content.
        var background = ResolveSlideBackground(slide, out bool backgroundIsImage);
        grid.Children.Add(new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(5)
        });

        // ── Layer 1: dark scrim over image backgrounds so white lyric text stays readable ──
        if (backgroundIsImage)
        {
            grid.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x59, 0x00, 0x00, 0x00)), // #000 @ ~35%
                CornerRadius = new CornerRadius(5)
            });
        }

        // ── Layer 2: slide LYRIC TEXT (center) — scales with zoom for WYSIWYG preview ──
        // Strip chords for clean readability (thumbnails show what audience sees)
        string slideText = _previewChordParser.StripChords(slide.Content.Text ?? "");
        var text = new TextBlock
        {
            Text = TruncateText(slideText, maxTextLength),
            FontSize = mainFontSize,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(Colors.White),
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = System.Windows.TextAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(8, sectionFontSize + 10, 8, numFontSize + 10),
            Effect = new System.Windows.Media.Effects.DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 3,
                ShadowDepth = 1,
                Opacity = 0.8
            }
        };

        // ── Layer 3: label chip (bottom-left) — e.g. "Verse 1" / "Chorus"; RED when live ──
        // Đợt 2 T8 / R5.4: when the slide's section carries a slide label, the chip shows
        // the LABEL's name on the LABEL's color; otherwise it keeps the plain section-name
        // treatment. The label color hex rides in the chip's Tag ("labelChip|<hex>") so
        // UpdateLabelChipLiveState can restore it after a live highlight pass.
        var slideLabel = SlideLabelNavigation.ResolveLabelForSlide(
            slide, labelSong?.Sections, EffectiveSlideLabels());
        Color? labelColor = null;
        if (slideLabel != null)
        {
            try
            {
                labelColor = (Color)ColorConverter.ConvertFromString(slideLabel.Color.Trim());
            }
            catch (FormatException) { labelColor = null; /* bad hex in catalog: plain chip */ }
        }

        var labelChip = new Border
        {
            Background = new SolidColorBrush(isActive
                ? LivePink
                : labelColor ?? Color.FromArgb(0xC0, 0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            Margin = new Thickness(5, 0, 0, 5),
            // Tagged so UpdateLiveSectionHighlights can recolor it on live changes.
            Tag = labelColor.HasValue ? $"labelChip|{slideLabel!.Color.Trim()}" : "labelChip",
            Child = new TextBlock
            {
                Text = slideLabel?.Name ?? (slide.SectionName ?? "").Trim(),
                FontSize = sectionFontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(isActive || labelColor.HasValue ? Colors.White : AccentCyan)
            }
        };
        // Hide the chip entirely when a slide has no section label.
        labelChip.Visibility = string.IsNullOrWhiteSpace(slide.SectionName)
            ? Visibility.Collapsed
            : Visibility.Visible;

        // ── Layer 4: number badge (bottom-right) ──
        var slideNum = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xC0, 0x00, 0x00, 0x00)),
            CornerRadius = new CornerRadius(3),
            Padding = new Thickness(5, 1, 5, 1),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = System.Windows.VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 5, 5),
            Child = new TextBlock
            {
                Text = slideNumber.ToString(),
                FontSize = numFontSize,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Colors.White)
            }
        };

        grid.Children.Add(text);
        grid.Children.Add(labelChip);
        grid.Children.Add(slideNum);

        border.Child = grid;

        int capturedIndex = slideNumber - 1;
        border.MouseLeftButtonDown += (s, ev) => onClick(capturedIndex);

        return border;
    }

    /// <summary>
    /// Scroll the center ScrollViewer so the given item's section is brought into view, WITHOUT
    /// clearing/replacing any sections. BringIntoView on the UI thread, non-blocking.
    /// </summary>
    private void ScrollToSection(ServiceItem item)
    {
        if (item == null) return;
        if (_sectionByItem.TryGetValue(item, out var el))
        {
            el.BringIntoView();
        }
    }

    /// <summary>
    /// Update border/label-chip highlights for the LIVE item's section ONLY (the engine's
    /// current slides belong to the live item). Finds the live item via
    /// <c>_engine.CurrentItemIndex</c>, locates its section in <see cref="_sectionByItem"/>,
    /// and recolors only that section's thumbnail WrapPanel. Non-live sections are never
    /// recolored. Falls back to a full <see cref="RefreshSlideSections"/> if the live section
    /// can't be found (stale map).
    /// </summary>
    private void UpdateLiveSectionHighlights()
    {
        if (_engine == null) return;

        var order = _engine.CurrentServiceOrder;
        int liveIndex = _engine.CurrentItemIndex;
        if (order == null || liveIndex < 0 || liveIndex >= order.Items.Count)
        {
            return;
        }

        var liveItem = order.Items[liveIndex];
        if (!_sectionByItem.TryGetValue(liveItem, out var sectionEl)
            || sectionEl is not StackPanel sectionRoot)
        {
            // Map stale — rebuild to recover.
            RefreshSlideSections();
            return;
        }

        // The thumbnail grid is the WrapPanel child of the section root.
        WrapPanel? thumbs = null;
        foreach (var child in sectionRoot.Children)
        {
            if (child is WrapPanel wp) { thumbs = wp; break; }
        }
        if (thumbs == null) return;

        int currentIndex = _engine.CurrentSlideIndex;

        for (int i = 0; i < thumbs.Children.Count; i++)
        {
            if (thumbs.Children[i] is Border border)
            {
                bool isActive = (i == currentIndex);
                bool isSelected = (_isEditMode && i == _selectedSlideIndex);

                // Live slide => RED (OwcLive) frame; edit-selected => cyan; otherwise neutral.
                border.BorderBrush = isActive
                    ? new SolidColorBrush(LivePink)
                    : isSelected
                        ? new SolidColorBrush((Color)ColorConverter.ConvertFromString("#0088AA"))
                        : new SolidColorBrush(BorderColor);
                border.BorderThickness = new Thickness(isActive ? 2 : isSelected ? 2 : 1);

                // Keep the label chip's live treatment in sync with the border.
                UpdateLabelChipLiveState(border, isActive);
            }
        }

        _lastActiveSlideIndex = currentIndex;
    }

    /// <summary>
    /// Recolor a thumbnail's label chip (tagged "labelChip", or "labelChip|&lt;hex&gt;" when
    /// the slide's section carries a slide label — đợt 2 T8) to match its live state:
    /// RED fill + white text when live; otherwise the label's own color with white text,
    /// or a dark chip with the cyan section label when no label applies.
    /// Walks the thumbnail's inner Grid; no-op if the chip isn't present.
    /// </summary>
    private void UpdateLabelChipLiveState(Border thumbnail, bool isActive)
    {
        if (thumbnail.Child is not Grid grid) return;

        foreach (var child in grid.Children)
        {
            if (child is Border chip && chip.Tag is string tag &&
                (tag == "labelChip" || tag.StartsWith("labelChip|", StringComparison.Ordinal)))
            {
                Color? labelColor = null;
                if (!isActive && tag.Length > "labelChip|".Length)
                {
                    try
                    {
                        labelColor = (Color)ColorConverter.ConvertFromString(tag["labelChip|".Length..]);
                    }
                    catch (FormatException) { labelColor = null; }
                }

                chip.Background = new SolidColorBrush(isActive
                    ? LivePink
                    : labelColor ?? Color.FromArgb(0xC0, 0x00, 0x00, 0x00));
                if (chip.Child is TextBlock chipText)
                {
                    chipText.Foreground = new SolidColorBrush(
                        isActive || labelColor.HasValue ? Colors.White : AccentCyan);
                }
                return;
            }
        }
    }

    /// <summary>
    /// The effective slide-label catalog for display/hotkeys (đợt 2 T8): the live settings
    /// catalog, or the seeded defaults when the settings predate slide labels (the T7
    /// null-catalog case) — display-only fallback, never mutates the settings.
    /// </summary>
    private List<SlideLabelDefinition> EffectiveSlideLabels()
        => _engine?.Settings?.SlideLabels ?? SlideLabelService.CreateDefaults();

    /// <summary>
    /// Slide-label hotkey jump (đợt 2 T8 / R5.4): when the live item is a song and the
    /// pressed letter matches a label hotkey, go to the first slide of the first section
    /// carrying that label (see <see cref="SlideLabelNavigation.FindFirstSlideIndexByHotkey"/>)
    /// and report the keystroke as handled. Behaviour choices, kept conservative so no
    /// existing Operator shortcut changes: only plain letter keys A–Z with no modifiers;
    /// digits stay reserved for the built-in jump-to-slide; "B" always stays GoBlack.
    /// Returns false when the keystroke is not a label jump (the caller's normal handling
    /// then applies unchanged).
    /// </summary>
    private bool TryHandleSlideLabelHotkey(Key key)
    {
        if (_engine?.CurrentSong == null) return false;
        if (_engine.CurrentItem?.Type != ServiceItemType.Song) return false;
        if (key is < Key.A or > Key.Z) return false;
        if (key == Key.B) return false; // B is the built-in GoBlack key — never repurposed.

        int target = SlideLabelNavigation.FindFirstSlideIndexByHotkey(
            _engine.CurrentSlides, _engine.CurrentSong.Sections, EffectiveSlideLabels(), key.ToString());
        if (target < 0) return false;

        _engine.GoToSlide(target);
        return true;
    }

    /// <summary>
    /// Resolve the BACKGROUND brush for a slide thumbnail, mirroring the live OutputWindow
    /// so the center grid previews real slide content. Resolution order:
    ///   1. slide-level background image (<see cref="SlideContent.BackgroundImage"/>)
    ///   2. slide theme-override: image, else its solid/gradient color
    ///   3. the current presenter theme: image, else its solid background color
    ///   4. a tasteful dark gradient default
    /// <paramref name="isImage"/> is true when the returned brush is a bitmap (so the caller
    /// draws a dark scrim for text readability).
    /// </summary>
    private Brush ResolveSlideBackground(Slide slide, out bool isImage)
    {
        isImage = false;

        // 1) Slide-level background image.
        var slideImage = TryLoadImageBrush(slide.Content?.BackgroundImage);
        if (slideImage != null) { isImage = true; return slideImage; }

        // 2) Slide theme override.
        if (slide.ThemeOverride is { } ov)
        {
            if (ov.BackgroundType == BackgroundType.Image)
            {
                var ovImage = TryLoadImageBrush(ov.BackgroundImage);
                if (ovImage != null) { isImage = true; return ovImage; }
            }
            var ovColor = TryParseColorBrush(ov.BackgroundColor);
            if (ovColor != null) return ovColor;
        }

        // 3) Current presenter theme.
        var theme = _themeManager?.CurrentTheme;
        if (theme != null)
        {
            if (theme.BackgroundType == BackgroundType.Image)
            {
                var themeImage = TryLoadImageBrush(theme.BackgroundImage);
                if (themeImage != null) { isImage = true; return themeImage; }
            }
            var themeColor = TryParseColorBrush(theme.BackgroundColor);
            if (themeColor != null) return themeColor;
        }

        // 4) Dark gradient default (top slightly lighter than bottom).
        return new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString("#2A2E3A"),
            (Color)ColorConverter.ConvertFromString("#12141C"),
            new System.Windows.Point(0, 0),
            new System.Windows.Point(0, 1));
    }

    /// <summary>
    /// Load an <see cref="ImageBrush"/> for a (possibly relative) image path, reusing the
    /// same base-dir resolution as the output window. Returns null on empty/missing/corrupt
    /// paths so background rendering never throws.
    /// </summary>
    private static ImageBrush? TryLoadImageBrush(string? path)
    {
        var resolved = ResolveImagePath(path);
        if (string.IsNullOrEmpty(resolved) || !File.Exists(resolved)) return null;
        try
        {
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            bmp.BeginInit();
            bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            bmp.UriSource = new Uri(resolved);
            bmp.EndInit();
            bmp.Freeze();
            return new ImageBrush(bmp) { Stretch = Stretch.UniformToFill };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[OperatorWindow] Thumbnail background load failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Parse a hex color into a SolidColorBrush. Returns null for empty/invalid input so the
    /// caller can fall through to the next background source.
    /// </summary>
    private static SolidColorBrush? TryParseColorBrush(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try
        {
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolve a possibly-relative image path against the app base dir (same rule as the
    /// output window) so relative slide/theme image paths load regardless of working dir.
    /// </summary>
    private static string? ResolveImagePath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (System.IO.Path.IsPathRooted(path)) return path;
        return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);
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
    private void TabSlides_Click(object sender, RoutedEventArgs e) => SwitchToTab("Slides");
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
        SlidesPanel.Visibility = Visibility.Collapsed;
        MediaPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        // Sidebar + center header follow SERVICE state, not the tab: once a service is loaded
        // they stay visible on every tab (Live/Services/Songs/Scripture/Media); hidden only
        // when nothing is loaded (fresh Home). The "Live" case below re-shows them explicitly
        // via ShowServiceEditor() regardless.
        SetSidebarVisible(_serviceOrderManager?.CurrentServiceOrder != null);

        // Reset all tab button styles
        SetTabInactive(TabLive);
        SetTabInactive(TabServices);
        SetTabInactive(TabSongs);
        SetTabInactive(TabSlides);
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
                        _songLibraryPanel.AddSongToServiceRequested += OnAddSongToServiceRequested;
                    }
                    SongsPanelContent.Content = _songLibraryPanel;
                    SongsPanel.Visibility = Visibility.Visible;
                    SetTabActive(TabSongs);
                    break;

                case "Slides":
                    if (_slidesLibraryPanel == null)
                    {
                        _slidesLibraryPanel = new SlidesLibraryPanel();
                        if (_slidePresentationService != null)
                            _slidesLibraryPanel.SlidePresentationService = _slidePresentationService;
                        // Edit (Stage 2) and Add-to-service (Stage 3) are stubbed — the panel
                        // raises these and the host shows a "coming soon" notice.
                        _slidesLibraryPanel.EditPresentationRequested += OnEditPresentationRequested;
                        _slidesLibraryPanel.AddPresentationToServiceRequested += OnAddPresentationToServiceRequested;
                        // Delete is routed through the host so it can warn about and remove the
                        // CURRENT-service items that live-link the deleted presentation (otherwise
                        // they would dangle). See OnDeletePresentationRequested.
                        _slidesLibraryPanel.DeletePresentationRequested += OnDeletePresentationRequested;
                    }
                    SlidesPanelContent.Content = _slidesLibraryPanel;
                    SlidesPanel.Visibility = Visibility.Visible;
                    SetTabActive(TabSlides);
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

    /// <summary>
    /// Songs tab "+ Add" button: add the chosen library song to the CURRENTLY LOADED service
    /// order, mirroring the Add/Edit Service Item dialog's song path (BuildTitle -> ApplySong ->
    /// AddItem -> SaveServiceOrderAsync -> RefreshServiceOrderList so the sidebar updates). With
    /// no service loaded it notifies and no-ops (never crashes); the whole body is guarded.
    /// </summary>
    private async void OnAddSongToServiceRequested(object? sender, SongListItem item)
    {
        try
        {
            if (_serviceOrderManager?.CurrentServiceOrder == null)
            {
                ShowToolError("Add Song", "Open or create a service first.");
                return;
            }
            if (_songManager == null) return;

            // Resolve robustly (normalized path, then folder name, then title) — same path
            // OnSongSelected uses — so folder-scan vs. registry casing differences don't miss.
            var song = SongPathResolver.Resolve(_songManager.Songs, item.Id, item.Title);
            if (song == null)
            {
                ShowToolError("Add Song",
                    $"Could not resolve song '{item.Title}' in the library.");
                return;
            }

            // Build the ServiceItem exactly like AddServiceItemDialog does for a Song, so the
            // sidebar label (Title) and link (SongFolder) match the picker.
            var title = ServiceItemMapper.BuildTitle(ServiceItemType.Song, null, song, null);
            var serviceItem = new ServiceItem { Type = ServiceItemType.Song, Title = title };
            ServiceItemMapper.ApplySong(serviceItem, song);
            serviceItem.Title = title;

            _serviceOrderManager.AddItem(serviceItem);
            await SaveServiceOrderAsync();
            RefreshServiceOrderList();
        }
        catch (Exception ex)
        {
            ShowToolError("Add Song", ex.Message);
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
    /// Informational "not yet available" notice for deferred (Stage 2/3) features. Uses the
    /// Information icon (NOT ShowToolError's red Error dialog) so a deliberately-deferred feature
    /// is not presented as a failure — matching the existing informational notices in this file.
    /// </summary>
    private void ShowComingSoon(string title, string message) =>
        MessageBox.Show(this, message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    // Opens the modal slide editor for the chosen presentation (Stage 2). Saving persists via the
    // service and refreshes the library grid; Close/X discards unsaved changes.
    private async void OnEditPresentationRequested(object? sender, SlidePresentationListItem item)
    {
        if (_slidePresentationService == null) return;

        var presentation = await _slidePresentationService.GetByIdAsync(item.Id);
        if (presentation is null)
        {
            ShowComingSoon("Edit Slides", "This presentation could not be found. It may have been deleted.");
            return;
        }

        var win = new SlideEditorWindow(presentation, _slidePresentationService) { Owner = this };
        if (win.ShowDialog() == true)
        {
            _slidesLibraryPanel?.Refresh();

            // Re-resolve linked presentations, then rebuild BOTH the sidebar and the center grid so an
            // edit (including a RENAME) to a presentation that a live-linked service item points at
            // shows immediately — the sidebar row label and the center section header pick up the
            // current presentation name from the freshly primed cache (closes the refresh gap).
            await PrimeSlidePresentationCacheAsync();
            RefreshServiceOrderList();
            RefreshSlideSections();
        }
    }

    // STAGE 3: add an authored Slides presentation to the current service order as a LIVE LINK.
    // Mirrors the Songs "+ Add" path (OnAddSongToServiceRequested): guard for a loaded service,
    // resolve the full presentation by id (the list item carries only Id/Name/SlideCount), convert
    // via the pure Core SlidePresentationMapper.ToServiceItemLink (which stores only
    // SlidePresentationId and leaves Slides EMPTY — no snapshot), AddItem -> SaveServiceOrderAsync
    // -> refresh sidebar. The presentation cache is primed before RefreshSlideSections so the new
    // live-linked section shows its current slides immediately in the center grid. The whole body
    // is guarded so it never crashes.
    //
    // When it refreshes: a live edit to a linked presentation is reflected (i) immediately after
    // returning from the slide editor (OnEditPresentationRequested primes + refreshes), (ii)
    // immediately after this add, and (iii) on the next service open / Slides-tab return / any
    // RefreshSlideSections that follows a cache prime. No polling is added.
    private async void OnAddPresentationToServiceRequested(object? sender, SlidePresentationListItem item)
    {
        try
        {
            if (_slidePresentationService == null) return;

            if (_serviceOrderManager?.CurrentServiceOrder == null)
            {
                ShowToolError("Add to Service", "Open or create a service first.");
                return;
            }

            var presentation = await _slidePresentationService.GetByIdAsync(item.Id);
            if (presentation is null)
            {
                ShowToolError("Add to Service",
                    "This presentation could not be found. It may have been deleted.");
                return;
            }

            var serviceItem = SlidePresentationMapper.ToServiceItemLink(presentation);

            _serviceOrderManager.AddItem(serviceItem);
            await SaveServiceOrderAsync();
            // Prime BEFORE rebuilding the sidebar so the newly added row shows the live presentation
            // name on first paint (the sidebar label resolves from the primed cache).
            await PrimeSlidePresentationCacheAsync();
            RefreshServiceOrderList();
            RefreshSlideSections();
        }
        catch (Exception ex)
        {
            ShowToolError("Add to Service", ex.Message);
        }
    }

    // Host-owned delete of a Slides-library presentation. A Slides service item is a LIVE LINK by
    // SlidePresentationId, so deleting the presentation would leave every current-service item that
    // points at it dangling (resolves to nothing → sidebar/center keep the stale stored Title
    // forever). This handler warns when N ≥ 1 current-service items link the presentation, deletes
    // the presentation, removes those now-dangling items from the open service, then re-primes the
    // cache and refreshes the sidebar/center + the library grid. Mirrors
    // OnAddPresentationToServiceRequested's guard/try-catch + manager → SaveServiceOrderAsync →
    // prime → refresh pattern. On "No" (or no service loaded and the user cancels) nothing changes.
    private async void OnDeletePresentationRequested(object? sender, SlidePresentationListItem item)
    {
        try
        {
            if (_slidePresentationService == null) return;

            var order = _serviceOrderManager?.CurrentServiceOrder;
            int linkedCount = SlideLinkReconciler.CountItemsLinkingPresentation(order, item.Id);

            var message = linkedCount > 0
                ? $"Delete the presentation \"{item.Name}\"? This presentation is used by {linkedCount} item(s) " +
                  "in the current service. Deleting it will also remove those item(s) from the service. " +
                  "This cannot be undone."
                : $"Delete the presentation \"{item.Name}\"? This cannot be undone.";

            var result = MessageBox.Show(
                this,
                message,
                "Delete Slides",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.Yes)
                return;

            await _slidePresentationService.DeleteAsync(item.Id);

            // Remove the now-dangling current-service items (Type == Slides &&
            // SlidePresentationId == deleted id). Remove by DESCENDING index so earlier removals do
            // not shift the indices of later ones. RemoveItem also purges the id from its group and
            // reorders (see ServiceOrderManager.RemoveItem).
            if (order != null && _serviceOrderManager != null)
            {
                var indices = new List<int>();
                for (int i = 0; i < order.Items.Count; i++)
                {
                    var si = order.Items[i];
                    if (si.Type == ServiceItemType.Slides && si.SlidePresentationId == item.Id)
                        indices.Add(i);
                }

                if (indices.Count > 0)
                {
                    for (int i = indices.Count - 1; i >= 0; i--)
                        _serviceOrderManager.RemoveItem(indices[i]);
                    await SaveServiceOrderAsync();
                }
            }

            // Re-resolve linked presentations and rebuild the sidebar + center so the removed rows
            // disappear immediately, then refresh the library grid so the deleted card is gone.
            await PrimeSlidePresentationCacheAsync();
            RefreshServiceOrderList();
            RefreshSlideSections();
            _slidesLibraryPanel?.RefreshGrid();
        }
        catch (Exception ex)
        {
            ShowToolError("Delete Slides", ex.Message);
        }
    }

    /// <summary>
    /// Resolve (into <see cref="_slidePresentationCache"/>) the current library presentation for
    /// every live-linked Slides item in the open service, so the synchronous
    /// <see cref="GetSlidesForItem"/> can convert their CURRENT slides on demand. This is the only
    /// place that awaits the async slide-presentation service; the render loop stays synchronous.
    ///
    /// The cache is cleared first so a presentation that was edited, renamed, or deleted elsewhere
    /// is re-resolved on the next prime. A presentation that is not found stores a null entry so the
    /// renderer falls back to the safe "no slides" note instead of crashing. Never throws into the
    /// UI — all failures are logged and swallowed.
    /// </summary>
    private async Task PrimeSlidePresentationCacheAsync()
    {
        try
        {
            _slidePresentationCache.Clear();

            if (_slidePresentationService == null) return;

            var order = _serviceOrderManager?.CurrentServiceOrder;
            if (order == null) return;

            var ids = order.Items
                .Where(i => i.Type == ServiceItemType.Slides && !string.IsNullOrEmpty(i.SlidePresentationId))
                .Select(i => i.SlidePresentationId!)
                .Distinct();

            foreach (var id in ids)
            {
                var presentation = await _slidePresentationService.GetByIdAsync(id);
                _slidePresentationCache[id] = presentation;
                // NOTE: stored ServiceItem.Title is intentionally NOT rewritten here. The live
                // DISPLAY name (sidebar row + center section header) is resolved at render time from
                // this cache via ServiceItemDisplayName.ResolveDisplayName, so a rename shows live
                // without mutating stored data.
            }

            // LOAD-TIME reconciliation: the cache now maps every live link to its presentation (or
            // null when it was deleted elsewhere). A non-null entry means the presentation still
            // exists; the set of those ids is exactly the "still exists" set. Any Slides item whose
            // non-empty link id is NOT in that set is an ORPHAN (dangling live link) — drop it from
            // the open service and persist the cleaned order so already-orphaned items (e.g. from a
            // prior out-of-app delete) disappear on the next service open. The pure
            // SlideLinkReconciler guarantees unlinked Slides items, resolvable Slides items, and all
            // non-Slides items are never touched. This removal is terminal for this pass — it does
            // NOT re-prime, so there is no recursion.
            if (_serviceOrderManager != null)
            {
                var existingIds = new HashSet<string>(
                    _slidePresentationCache.Where(kvp => kvp.Value != null).Select(kvp => kvp.Key));

                var orphanIndices = SlideLinkReconciler.FindOrphanedSlideItemIndices(order, existingIds);
                if (orphanIndices.Count > 0)
                {
                    for (int i = orphanIndices.Count - 1; i >= 0; i--)
                        _serviceOrderManager.RemoveItem(orphanIndices[i]);
                    await _serviceOrderManager.SaveAsync();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"PrimeSlidePresentationCacheAsync: failed to prime cache: {ex.Message}");
        }
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
