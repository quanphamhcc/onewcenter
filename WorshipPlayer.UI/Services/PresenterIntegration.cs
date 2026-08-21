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

    // Windows
    private OperatorWindow? _operatorWindow;
    private OutputWindow? _outputWindow;

    /// <summary>
    /// Fired when a ServiceOrder item wants to load a song in the backtrack player.
    /// The string parameter is the song folder path from the ServiceItem.
    /// </summary>
    public event EventHandler<string>? SongLoadRequested;

    public PresenterIntegration(
        AudioEngine audioEngine,
        MidiCueService midiCueService,
        AppSettings appSettings,
        PresenterSettings presenterSettings,
        SongManager songManager)
    {
        _audioEngine = audioEngine;
        _midiCueService = midiCueService;
        _appSettings = appSettings;
        _presenterSettings = presenterSettings;
        _songManager = songManager;

        // Create ServiceOrderManager (uses ServiceOrders folder next to app)
        var appFolder = AppDomain.CurrentDomain.BaseDirectory;
        var serviceOrdersPath = Path.Combine(appFolder, "ServiceOrders");
        if (!Directory.Exists(serviceOrdersPath))
            Directory.CreateDirectory(serviceOrdersPath);
        ServiceOrderManager = new ServiceOrderManager(serviceOrdersPath);

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

        // Create additional services for panels and tools
        var usagePath = Path.Combine(appFolder, "song-usage.json");
        SongUsageService = new SongUsageService(usagePath);
        SongLibraryService = new SongLibraryService(_songManager, SongUsageService);

        var mediaPath = Path.Combine(appFolder, "Media");
        MediaLibraryService = new MediaLibraryService(mediaPath);

        GlobalSearchService = new GlobalSearchService(_songManager, ServiceOrderManager);
        ExportService = new ExportService();

        var alertTemplatesPath = Path.Combine(appFolder, "alert-templates.json");
        AlertService = new AlertService(alertTemplatesPath);

        CountdownService = new CountdownService();

        var biblePath = Path.Combine(appFolder, "bible.db");
        BibleDatabaseService = new BibleDatabaseService(biblePath);
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

        // Wire ServiceOrderManager item changes for song auto-loading
        ServiceOrderManager.ItemChanged += OnServiceItemChanged;

        // Wire PresenterEngine slide changes to broadcast to stage display
        PresenterEngine.SlideChanged += OnPresenterSlideChanged;

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
            await SongUsageService.LogUsageAsync(song.Title, song.FolderPath, serviceName, serviceDate);
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
                _songManager);
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
                await StageDisplayServer.StartAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Stage server start failed: {ex.Message}");
            }
        }
    }

    // ---- Private event handlers ----

    private void OnAudioEngineSectionChanged(object? sender, SectionChangedEventArgs e)
    {
        var sectionName = e.NewSection?.Name ?? "";
        MidiPresenterSync.OnBacktrackSectionChanged(sectionName);

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
            var appFolder = AppDomain.CurrentDomain.BaseDirectory;
            var path = Path.Combine(appFolder, "presenter-settings.json");
            var json = System.Text.Json.JsonSerializer.Serialize(settings,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to save presenter settings: {ex.Message}");
        }

        // Apply the display change immediately (don't require restart)
        ApplyDisplaySettings(settings);
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
            }

            _outputWindow.PlaceOnDisplay(outputDisplay);

            if (!_outputWindow.IsVisible)
                _outputWindow.Show();
        }
        else
        {
            // "None" selected — close the output window if it's open
            if (_outputWindow != null && _outputWindow.IsLoaded)
            {
                _outputWindow.ExitFullScreen();
                _outputWindow.Close();
                _outputWindow = null;
            }
        }

        // Update OperatorWindow's reference so it forwards slides to the correct output
        _operatorWindow?.SetOutputWindow(_outputWindow);
    }

    private void BroadcastStageUpdate()
    {
        // Build stage display data from current presenter state and broadcast to connected stage clients
        var data = new StageDisplayData
        {
            CurrentText = PresenterEngine.CurrentSlide?.Content?.Text ?? "",
            NextText = PresenterEngine.NextSlide?.Content?.Text ?? "",
            SectionName = PresenterEngine.CurrentSlide?.SectionName ?? "",
            SongTitle = PresenterEngine.CurrentSong?.Title ?? "",
            Key = PresenterEngine.CurrentSong?.OriginalKey ?? "",
            OriginalKey = PresenterEngine.CurrentSong?.OriginalKey ?? "",
            IsLive = PresenterEngine.IsLive,
            IsBlacked = PresenterEngine.IsBlacked,
            CurrentSlideIndex = PresenterEngine.CurrentSlideIndex,
            TotalSlides = PresenterEngine.TotalSlides
        };

        // Fire and forget - stage broadcast is best-effort
        _ = StageDisplayServer.BroadcastSlideUpdate(data);
    }

    public void Dispose()
    {
        // Unwire events
        _audioEngine.SectionChanged -= OnAudioEngineSectionChanged;
        _audioEngine.PositionChanged -= OnAudioEnginePositionChanged;
        _audioEngine.PlaybackStopped -= OnAudioEnginePlaybackStopped;
        ServiceOrderManager.ItemChanged -= OnServiceItemChanged;
        PresenterEngine.SlideChanged -= OnPresenterSlideChanged;

        // Dispose services
        MidiPresenterSync.Dispose();
        PresenterEngine.Dispose();
        StageDisplayServer.Dispose();
        CountdownService.Dispose();

        // Close windows
        try { _operatorWindow?.Close(); } catch { }
        try { _outputWindow?.Close(); } catch { }
    }
}
