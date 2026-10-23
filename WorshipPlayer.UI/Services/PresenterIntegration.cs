using System.IO;
using WorshipPlayer.Audio.Engine;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Core.Services.Bible;
using WorshipPlayer.Core.Services.Interfaces;
using WorshipPlayer.Presenter.Services;
using WorshipPlayer.Presenter.Views;

namespace WorshipPlayer.UI.Services;

/// <summary>
/// Integrates the Presenter module with the Backtrack Player's MainWindow.
/// 
/// Acts as the bridge between:
/// - AudioEngine events (section changes, position updates, playback stopped)
/// - MidiPresenterSync (translates backtrack events into slide actions)
/// - PresenterEngine (manages slide state)
/// - ServiceOrderManager (navigates service items)
/// - DisplayManager / StageDisplayServer (output display management)
/// - ThemeManager (slide themes)
/// 
/// Lifecycle: created once in MainWindow_Loaded, disposed in MainWindow_Closing.
/// </summary>
public class PresenterIntegration : IDisposable
{
    private readonly AudioEngine _audioEngine;
    private readonly MidiCueService _midiCueService;
    private readonly AppSettings _appSettings;
    private readonly PresenterSettings _presenterSettings;
    private readonly PresenterSettingsService _presenterSettingsService;
    private readonly SongManager _songManager;

    // Presenter services
    public PresenterEngine PresenterEngine { get; }
    public MidiPresenterSync MidiPresenterSync { get; }
    public ServiceOrderManager ServiceOrderManager { get; }
    public DisplayManager DisplayManager { get; }
    public StageDisplayServer StageDisplayServer { get; }
    public ThemeManager ThemeManager { get; }

    // Additional services for panels and tools
    public SongLibraryService SongLibraryService { get; }
    public MediaLibraryService MediaLibraryService { get; }
    public GlobalSearchService GlobalSearchService { get; }
    public ExportService ExportService { get; }
    public AlertService AlertService { get; }
    public CountdownService CountdownService { get; }
    public SongUsageService SongUsageService { get; }
    public BibleDatabaseService BibleDatabaseService { get; }
    public ITemplateService TemplateService { get; }
    public ISlidePresentationService SlidePresentationService { get; }

    // Windows
    private OperatorWindow? _operatorWindow;
    private OutputWindow? _outputWindow;

    /// <summary>
    /// Fired when a ServiceOrder item wants to load a song in the backtrack player.
    /// The string parameter is the song folder path from the ServiceItem.
    /// </summary>
    public event EventHandler<string>? SongLoadRequested;

    /// <summary>
    /// Fired with a non-fatal, operator-visible status message (e.g. the stage server could not
    /// start on its port). The host (MainWindow) routes this to its status bar. Best-effort only.
    /// </summary>
    public event EventHandler<string>? StatusMessage;

    public PresenterIntegration(
        AudioEngine audioEngine,
        MidiCueService midiCueService,
        AppSettings appSettings,
        PresenterSettings presenterSettings,
        PresenterSettingsService presenterSettingsService,
        SongManager songManager)
    {
        _audioEngine = audioEngine;
        _midiCueService = midiCueService;
        _appSettings = appSettings;
        _presenterSettings = presenterSettings;
        _presenterSettingsService = presenterSettingsService;
        _songManager = songManager;

        // Shared SQLite application database path (app.db next to the app), reused by the
        // service-order / song-usage / media / alert / template stores below.
        var appFolder = AppDomain.CurrentDomain.BaseDirectory;
        var appDbPath = Path.Combine(appFolder, "app.db");

        // Create ServiceOrderManager (JSON files under the ServiceOrders folder stay
        // authoritative; the shared app.db is a reconciled mirror — Phase 5 SQL migration).
        var serviceOrdersPath = Path.Combine(appFolder, "ServiceOrders");
        if (!Directory.Exists(serviceOrdersPath))
            Directory.CreateDirectory(serviceOrdersPath);
        ServiceOrderManager = new ServiceOrderManager(serviceOrdersPath, appDbPath);

        // Create PresenterEngine
        PresenterEngine = new PresenterEngine(ServiceOrderManager, _presenterSettings);

        // Create MidiPresenterSync bridge
        MidiPresenterSync = new MidiPresenterSync(PresenterEngine, _midiCueService, _presenterSettings);

        // Create DisplayManager
        DisplayManager = new DisplayManager(_presenterSettings);

        // Create StageDisplayServer
        StageDisplayServer = new StageDisplayServer(_presenterSettings.StageServerPort);

        // Create ThemeManager
        ThemeManager = new ThemeManager();

        // Create additional services for panels and tools.
        // Song usage is persisted in the shared SQLite app.db (migrated from song-usage.json),
        // constructed the same way the Bible db is below (reuses appDbPath from above).
        SongUsageService = new SongUsageService(appDbPath);
        SongLibraryService = new SongLibraryService(_songManager, SongUsageService);

        // Media asset binaries stay on disk under the Media folder; their metadata is persisted
        // in the shared SQLite app.db (migrated from the per-asset *_metadata.json sidecars),
        // reusing the same appDbPath as the sibling services above.
        var mediaPath = Path.Combine(appFolder, "Media");
        MediaLibraryService = new MediaLibraryService(mediaPath, appDbPath);

        GlobalSearchService = new GlobalSearchService(_songManager, ServiceOrderManager);
        ExportService = new ExportService();

        // Alert templates are persisted in the shared SQLite app.db (migrated from
        // alert-templates.json), constructed the same way SongUsageService is above.
        AlertService = new AlertService(appDbPath);

        CountdownService = new CountdownService();

        var biblePath = Path.Combine(appFolder, "bible.db");
        BibleDatabaseService = new BibleDatabaseService(biblePath);

        // Custom slide templates are persisted in the shared SQLite app.db (migrated from
        // the Templates/*.json folder); built-ins stay code-seeded.
        TemplateService = new TemplateService(appDbPath);

        // Slides-library presentations are persisted in the shared SQLite app.db (new
        // SlidePresentations table), constructed the same way the sibling services above are
        // (reuses the same appDbPath).
        SlidePresentationService = new SlidePresentationService(appDbPath);
    }

    /// <summary>
    /// Wire up AudioEngine events to the presenter pipeline.
    /// Call this after the AudioEngine is fully initialized.
    /// </summary>
    public void Initialize()
    {
        // Activate the MIDI sync bridge
        MidiPresenterSync.Activate();

        // Wire AudioEngine events
        _audioEngine.SectionChanged += OnAudioEngineSectionChanged;
        _audioEngine.PositionChanged += OnAudioEnginePositionChanged;
        _audioEngine.PlaybackStopped += OnAudioEnginePlaybackStopped;

        // F4/F11: an infinite section loop in the mixer re-raises AudioEngine.SectionLooped
        // (section NAME payload); forward it through the sync bridge so the presenter resets
        // to the section's first slide.
        _audioEngine.SectionLooped += OnAudioEngineSectionLooped;

        // Wire ServiceOrderManager item changes for song auto-loading
        ServiceOrderManager.ItemChanged += OnServiceItemChanged;

        // F1: when a service order is loaded, set the first item current (fires ItemChanged so the
        // operator list + engine pick it up) — LoadAsync itself only fires ServiceOrderLoaded.
        ServiceOrderManager.ServiceOrderLoaded += OnServiceOrderLoaded;

        // Wire PresenterEngine slide changes to broadcast to stage display
        PresenterEngine.SlideChanged += OnPresenterSlideChanged;

        // F5: re-broadcast stage data on pitch change so the web stage display re-transposes.
        PresenterEngine.PitchShiftChanged += OnPresenterPitchShiftChanged;

        // Refresh display info
        DisplayManager.RefreshDisplays();

        // Initialize Bible database in the background
        _ = InitializeBibleDatabaseAsync();
    }

    /// <summary>
    /// Initialize the Bible database with schema and seed data.
    /// </summary>
    private async Task InitializeBibleDatabaseAsync()
    {
        try
        {
            await BibleDatabaseService.InitializeAsync();
            await BibleDatabaseService.SeedKjvVersesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Bible database init failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Notify the presenter that a song was loaded in the backtrack player.
    /// </summary>
    public void OnSongLoaded(Song song)
    {
        MidiPresenterSync.OnSongLoaded(song);
        PresenterEngine.OnSongLoaded(song);

        // Log song usage for CCLI reporting
        _ = LogSongUsageAsync(song);

        // Broadcast to stage display server
        BroadcastStageUpdate();
    }

    /// <summary>
    /// Log song usage when a song is loaded for playback.
    /// </summary>
    private async Task LogSongUsageAsync(Song song)
    {
        try
        {
            var serviceName = ServiceOrderManager.CurrentServiceOrder?.Name ?? "Unknown Service";
            var serviceDate = DateTime.TryParse(ServiceOrderManager.CurrentServiceOrder?.Date, out var d) ? d : DateTime.Today;
            // Use the record overload (via the testable Core mapper) so the song's Artist and
            // CCLI number are captured into each SongUsageRecord for CCLI reporting.
            await SongUsageService.LogUsageAsync(SongUsageMapper.FromSong(song, serviceName, serviceDate));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Song usage logging failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Notify the presenter that pitch shift changed.
    /// </summary>
    public void OnPitchShiftChanged(int semitones)
    {
        MidiPresenterSync.OnPitchShiftChanged(semitones);
    }

    /// <summary>
    /// Notify the presenter that tempo changed.
    /// </summary>
    public void OnTempoChanged(double percent)
    {
        MidiPresenterSync.OnTempoChanged(percent);
    }

    /// <summary>
    /// Open the presenter operator window. Output window is NOT created here —
    /// created only when user explicitly configures a display.
    /// </summary>
    public void OpenPresenterWindow()
    {
        // Do NOT create OutputWindow here — it was causing StackOverflow
        // due to VLC VideoView or other XAML rendering issues.
        // Output will be created via ApplyDisplaySettings when user configures a display.

        // Create operator window (ALWAYS opens — this is the main control panel)
        if (_operatorWindow == null || !_operatorWindow.IsLoaded)
        {
            _operatorWindow = new OperatorWindow();
            _operatorWindow.Initialize(PresenterEngine, _outputWindow, ThemeManager, DisplayManager, SavePresenterSettings);
            _operatorWindow.SetServices(
                SongLibraryService,
                MediaLibraryService,
                GlobalSearchService,
                ExportService,
                AlertService,
                CountdownService,
                SongUsageService,
                BibleDatabaseService,
                ServiceOrderManager,
                _songManager,
                TemplateService,
                SlidePresentationService);
            // F6: give the operator window the single-monitor Go-Live hooks.
            _operatorWindow.SetGoLiveHandlers(HandleGoLiveSingleMonitor, () => IsSingleMonitorOutput);
            _operatorWindow.Closed += (s, e) => _operatorWindow = null;
            _operatorWindow.Show();
        }
        else
        {
            // Bring existing window to front
            _operatorWindow.Activate();
        }
    }

    /// <summary>
    /// Start the embedded stage display web server if enabled in settings.
    /// </summary>
    public async Task StartStageServerAsync()
    {
        if (_presenterSettings.StageServerEnabled)
        {
            try
            {
                var result = await StageDisplayServer.StartAsync();
                if (!result.Started)
                {
                    // Non-fatal: the live congregation path does not depend on the server.
                    var message = $"Stage display server could not start on port {_presenterSettings.StageServerPort} — the service will run without the second-screen/remote display";
                    System.Diagnostics.Debug.WriteLine($"{message} ({result.Error})");
                    StatusMessage?.Invoke(this, message);
                }
            }
            catch (Exception ex)
            {
                // StartAsync is designed not to throw out of the normal path, but stay defensive.
                System.Diagnostics.Debug.WriteLine($"Stage server start failed: {ex.Message}");
                StatusMessage?.Invoke(this, $"Stage display server could not start on port {_presenterSettings.StageServerPort} — the service will run without the second-screen/remote display");
            }
        }
    }

    /// <summary>
    /// Auto-load the most recent *.json service order in the ServiceOrders folder, if any.
    /// Called on startup (from MainWindow after the display manager is populated). Any
    /// null/exception from the load is degraded to a non-fatal status message, never a crash.
    /// The resulting first-item-current may raise SongLoadRequested; MainWindow.LoadSong's
    /// existing try/catch degrades missing stems to a non-fatal status (F1/F9).
    /// </summary>
    public async Task AutoLoadMostRecentServiceOrderAsync()
    {
        try
        {
            var appFolder = AppDomain.CurrentDomain.BaseDirectory;
            var serviceOrdersPath = Path.Combine(appFolder, "ServiceOrders");
            if (!Directory.Exists(serviceOrdersPath)) return;

            var mostRecent = new DirectoryInfo(serviceOrdersPath)
                .GetFiles("*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .FirstOrDefault();

            if (mostRecent == null) return;

            var loaded = await ServiceOrderManager.LoadAsync(mostRecent.FullName);
            if (loaded == null)
            {
                StatusMessage?.Invoke(this, $"Could not load service order '{mostRecent.Name}'");
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Auto-load service order failed: {ex.Message}");
            StatusMessage?.Invoke(this, "Could not auto-load the most recent service order");
        }
    }

    /// <summary>
    /// Clamp the saved OutputDisplay/StageDisplay indices against the currently registered
    /// monitors (F6/F7). Called from MainWindow right after displays are registered so a saved
    /// index of 1 on a now-single-monitor machine falls back to -1 (unassigned) instead of the
    /// silent "configured but no bounds" no-output branch.
    /// </summary>
    public void ClampDisplayIndicesToMonitors()
    {
        int monitorCount = DisplayManager.AvailableDisplays.Count;
        _presenterSettings.OutputDisplay = DisplayManager.ClampDisplayIndex(_presenterSettings.OutputDisplay, monitorCount);
        _presenterSettings.StageDisplay = DisplayManager.ClampDisplayIndex(_presenterSettings.StageDisplay, monitorCount);
    }

    // ---- Private event handlers ----

    private void OnAudioEngineSectionChanged(object? sender, SectionChangedEventArgs e)
    {
        var sectionName = e.NewSection?.Name ?? "";
        MidiPresenterSync.OnBacktrackSectionChanged(sectionName);

        BroadcastStageUpdate();
    }

    private void OnAudioEngineSectionLooped(object? sender, string sectionName)
    {
        // F4/F11: an infinite section loop reset — forward the section name through the sync
        // bridge so the presenter re-shows that section's first slide.
        MidiPresenterSync.OnSectionLooped(sectionName);

        BroadcastStageUpdate();
    }

    private void OnServiceOrderLoaded(object? sender, ServiceOrder order)
    {
        // F1: set the first item current (fires ItemChanged so the engine + operator list react).
        // LoadAsync set CurrentItemIndex but did not fire ItemChanged.
        if (order.Items.Count > 0)
        {
            ServiceOrderManager.GoToItem(0);
        }
    }

    private void OnPresenterPitchShiftChanged(object? sender, PitchShiftChangedEventArgs e)
    {
        // F5: push the re-transposed stage data to the web stage display.
        BroadcastStageUpdate();
    }

    private void OnPresenterSlideChanged(object? sender, SlideChangedEventArgs e)
    {
        // Broadcast to stage display every time a slide changes
        // (manual click, timestamp auto-advance, MIDI cue, section jump — all sources)
        BroadcastStageUpdate();
    }

    private void OnAudioEnginePositionChanged(object? sender, PlaybackPositionEventArgs e)
    {
        MidiPresenterSync.OnPlaybackPositionUpdate(e.PositionMs);
    }

    private void OnAudioEnginePlaybackStopped(object? sender, EventArgs e)
    {
        MidiPresenterSync.OnSongEnded();
    }

    private void OnServiceItemChanged(object? sender, ServiceItemChangedEventArgs e)
    {
        if (e.NewItem == null) return;

        if (e.NewItem.Type == ServiceItemType.Song && e.NewItem.UseBacktrack)
        {
            // Song item with backtrack - request the MainWindow to load it
            if (!string.IsNullOrEmpty(e.NewItem.SongFolder))
            {
                SongLoadRequested?.Invoke(this, e.NewItem.SongFolder);
            }
        }
        // For all items (song or non-song), the PresenterEngine is already
        // subscribed to ServiceOrderManager.ItemChanged internally
    }

    private void SavePresenterSettings(PresenterSettings settings)
    {
        try
        {
            // Persistence is owned by the single PresenterSettingsService (file-backed).
            _presenterSettingsService.Settings = settings;
            _presenterSettingsService.SaveAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save presenter settings: {ex.Message}");
        }

        // Apply the display change immediately (don't require restart)
        ApplyDisplaySettings(settings);
    }

    /// <summary>
    /// Đợt 2 T4 / R3: a video on the output window ended (full-screen playback or
    /// the current item's background video) — forward it to the engine's VideoEnd
    /// auto-advance trigger. Whether the current item actually advances is decided
    /// by the engine + the pure AutoAdvanceResolver (condition must be VideoEnd).
    /// </summary>
    private void OnOutputVideoEnded(object? sender, EventArgs e)
    {
        PresenterEngine.NotifyCurrentItemVideoEnded();
    }

    /// <summary>
    /// Apply display settings immediately — move/create/close the Output Window
    /// based on the current OutputDisplay setting.
    /// </summary>
    private void ApplyDisplaySettings(PresenterSettings settings)
    {
        var outputDisplay = DisplayManager.GetOutputBounds();

        if (settings.OutputDisplay >= 0 && outputDisplay != null)
        {
            // A display is configured — show output window on that monitor
            if (_outputWindow == null || !_outputWindow.IsLoaded)
            {
                _outputWindow = new OutputWindow();
                _outputWindow.Closed += (s, e) => _outputWindow = null;
                _outputWindow.VideoPlaybackEnded += OnOutputVideoEnded;
                _outputWindow.BackgroundVideoEnded += OnOutputVideoEnded;
            }

            // Dual-monitor / explicitly-configured output: preserve the existing close semantics
            // (closing the dedicated output monitor's window destroys it as before).
            _outputWindow.SuppressDestroyWhileLive = false;

            _outputWindow.PlaceOnDisplay(outputDisplay);

            if (!_outputWindow.IsVisible)
                _outputWindow.Show();
        }
        else
        {
            // "None" selected — close the output window if it's open
            if (_outputWindow != null && _outputWindow.IsLoaded)
            {
                // Genuine teardown: ensure the close actually destroys (OnClosed disposes VLC).
                _outputWindow.SuppressDestroyWhileLive = false;
                _outputWindow.ExitFullScreen();
                _outputWindow.Close();
                _outputWindow = null;
            }
        }

        // Update OperatorWindow's reference so it forwards slides to the correct output
        _operatorWindow?.SetOutputWindow(_outputWindow);
    }

    /// <summary>
    /// F6: handle Go-Live on a single-monitor machine. When no separate output monitor is
    /// configured (OutputDisplay &lt; 0) or only one monitor is present, lazily create the output
    /// window and full-screen it on the sole monitor with Topmost=false so the operator can
    /// Alt-Tab back to the command center. On a dual-monitor rig with a configured output this is
    /// a no-op (ApplyDisplaySettings already placed the window). Never throws; a bad DisplayInfo
    /// falls back to the primary monitor.
    /// </summary>
    public void HandleGoLiveSingleMonitor()
    {
        try
        {
            bool singleMonitor = _presenterSettings.OutputDisplay < 0 || DisplayManager.AvailableDisplays.Count <= 1;
            if (!singleMonitor) return;

            // Lazily create the output window (same pattern as ApplyDisplaySettings).
            if (_outputWindow == null || !_outputWindow.IsLoaded)
            {
                _outputWindow = new OutputWindow();
                _outputWindow.Closed += (s, e) => _outputWindow = null;
                _outputWindow.VideoPlaybackEnded += OnOutputVideoEnded;
                _outputWindow.BackgroundVideoEnded += OnOutputVideoEnded;
            }

            if (!_outputWindow.IsVisible)
                _outputWindow.Show();

            // Full-screen but NOT topmost so the operator can return to the command center.
            _outputWindow.GoFullScreen(topmost: false);

            // While LIVE on a single monitor, a user close ([X]) should hide (keep the one instance
            // + its LibVLC services alive) rather than destroy/orphan it; GO LIVE then re-shows it.
            // The flag is cleared on a genuine non-live reconfigure (ApplyDisplaySettings) and on
            // Dispose so app shutdown still destroys normally.
            _outputWindow.SuppressDestroyWhileLive = true;

            // Register with the operator window so slides forward to this output.
            _operatorWindow?.SetOutputWindow(_outputWindow);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Single-monitor Go-Live failed: {ex.Message}");
        }
    }

    /// <summary>
    /// F6: whether the output is currently a single-monitor full-screen window (no dedicated
    /// output monitor configured). The OperatorWindow uses this to decide whether Esc should
    /// ExitFullScreen (reclaim the screen) rather than content-black.
    /// </summary>
    public bool IsSingleMonitorOutput =>
        (_presenterSettings.OutputDisplay < 0 || DisplayManager.AvailableDisplays.Count <= 1)
        && _outputWindow != null;

    private void BroadcastStageUpdate()
    {
        // F12: take a single internally-consistent snapshot under the engine lock (this runs on the
        // audio render thread for section/position/loop events, so a torn read would be possible
        // without the lock). F5: SnapshotStageData reflects the current state including pitch-driven
        // re-renders downstream.
        var data = PresenterEngine.SnapshotStageData();

        // Fire and forget - stage broadcast is best-effort
        _ = StageDisplayServer.BroadcastSlideUpdate(data);
    }

    public void Dispose()
    {
        // Unwire events
        _audioEngine.SectionChanged -= OnAudioEngineSectionChanged;
        _audioEngine.PositionChanged -= OnAudioEnginePositionChanged;
        _audioEngine.PlaybackStopped -= OnAudioEnginePlaybackStopped;
        _audioEngine.SectionLooped -= OnAudioEngineSectionLooped;
        ServiceOrderManager.ItemChanged -= OnServiceItemChanged;
        ServiceOrderManager.ServiceOrderLoaded -= OnServiceOrderLoaded;
        PresenterEngine.SlideChanged -= OnPresenterSlideChanged;
        PresenterEngine.PitchShiftChanged -= OnPresenterPitchShiftChanged;

        // Dispose services
        MidiPresenterSync.Dispose();
        PresenterEngine.Dispose();
        StageDisplayServer.Dispose();
        CountdownService.Dispose();

        // Close windows — app shutdown must destroy the output (and dispose its VLC services),
        // so clear the hide-while-live guard before closing.
        try { _operatorWindow?.Close(); } catch { }
        try
        {
            if (_outputWindow != null)
            {
                _outputWindow.SuppressDestroyWhileLive = false;
                _outputWindow.Close();
            }
        }
        catch { }
    }
}
