using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WorshipPlayer.Audio.Engine;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.UI.Services;

namespace WorshipPlayer.UI.Views;

public partial class MainWindow : Window
{
    private AppSettings _settings = new();
    private SettingsService _settingsService = null!;
    private SongManager _songManager = null!;
    private AudioEngine _audioEngine = null!;
    private LightingService _lightingService = null!;
    private MidiCueService _midiCueService = null!;
    private PresenterIntegration? _presenterIntegration;
    private PresenterSettingsService _presenterSettingsService = null!;

    private Song? _currentSong;
    private bool _suppressSetlistSelection = false;
    private int _currentSectionIndex = -1;
    // Monotonic token for LoadSong. LoadSong is async (it decodes audio on a background thread), so
    // two loads started close together can finish out of order and the LATER-finishing one would
    // win — activating the WRONG song's mixer (a cause of "played a different song"). Each call
    // captures a token; after the await, only the newest token is allowed to activate its mixer.
    private int _loadGeneration = 0;
    private bool _loopEnabled = false;
    private bool _loopInfinite = false;
    private double _tempoPercent = 100.0;
    private int _pitchSemitones = 0;

    private readonly DispatcherTimer _uiTimer;
    private readonly string[] _noteNames = { "C", "Db", "D", "Eb", "E", "F", "Gb", "G", "Ab", "A", "Bb", "B" };

    public MainWindow()
    {
        InitializeComponent();

        // UI update timer
        _uiTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50) // 20 FPS UI updates
        };
        _uiTimer.Tick += UITimer_Tick;

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private TrainingDataService _trainingDataService = null!;

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Initialize services - try multiple paths to find Songs folder
        var appFolder = AppDomain.CurrentDomain.BaseDirectory;
        var appDbPath = Path.Combine(appFolder, "app.db");

        _settingsService = new SettingsService(appDbPath);
        await _settingsService.LoadAsync();
        _settings = _settingsService.Settings;

        // ─── AI Training Data folder: resolve silently, never prompt on startup ───
        // Covers empty/whitespace and a stale saved path on a missing drive (IsUsable == false).
        if (!TrainingDataPaths.IsUsable(_settings.TrainingDataFolder))
        {
            _settings.TrainingDataFolder = TrainingDataPaths.DefaultFolder();
            await _settingsService.SaveAsync();
        }

        // Ensure folder exists
        if (!string.IsNullOrEmpty(_settings.TrainingDataFolder))
        {
            try { Directory.CreateDirectory(_settings.TrainingDataFolder); } catch { }
        }

        _trainingDataService = new TrainingDataService(_settings.TrainingDataFolder);

        // ─── SONGS FOLDER: Use saved path or prompt on first launch ───────
        string songsPath;
        string setlistsPath;

        if (!string.IsNullOrEmpty(_settings.SongsFolder) && Directory.Exists(_settings.SongsFolder))
        {
            songsPath = _settings.SongsFolder;
        }
        else
        {
            // Prompt user for Songs folder location
            var songsDialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select your Songs folder (contains subfolders with audio stems)"
            };

            if (songsDialog.ShowDialog(this) == true)
            {
                songsPath = songsDialog.FolderName;
                _settings.SongsFolder = songsPath;
                await _settingsService.SaveAsync();
            }
            else
            {
                // Fallback: try to find next to app
                songsPath = FindFolder("Songs", appFolder);
            }
        }

        if (!string.IsNullOrEmpty(_settings.SetlistsFolder) && Directory.Exists(_settings.SetlistsFolder))
        {
            setlistsPath = _settings.SetlistsFolder;
        }
        else
        {
            setlistsPath = FindFolder("Setlists", appFolder);
            // Create if doesn't exist
            try { Directory.CreateDirectory(setlistsPath); } catch { }
        }

        // JSON song/setlist files stay authoritative; app.db is a reconciled mirror (Phase 6).
        _songManager = new SongManager(songsPath, setlistsPath, appDbPath);
        await _songManager.LoadAllSongsAsync();
        await _songManager.LoadAllSetlistsAsync();

        // Initialize audio engine
        _audioEngine = new AudioEngine(_settings);
        try
        {
            _audioEngine.Initialize();
            StatusText.Text = $"Audio OK | Songs: {_songManager.Songs.Count} found in {songsPath} | [SPACE]=Play [L]=Loop [Arrows]=Sections [1-9]=Jump";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Audio init failed: {ex.Message} - Trying shared mode...";
            _settings.WasapiExclusive = false;
            try
            {
                _audioEngine.Initialize();
                StatusText.Text = $"Audio OK (shared) | Songs: {_songManager.Songs.Count} | [SPACE]=Play [L]=Loop [Arrows]=Sections";
            }
            catch (Exception ex2)
            {
                StatusText.Text = $"Audio error: {ex2.Message}";
            }
        }

        // Initialize lighting
        _lightingService = new LightingService(_settings);
        _lightingService.Initialize();
        UpdateOscStatus();

        // Initialize MIDI Cue Service (WorshipTools Presenter integration)
        _midiCueService = new MidiCueService(_settings);
        _midiCueService.SetMidiOutput(new WorshipPlayer.Audio.DSP.MidiCueOutput());
        if (_settings.MidiPresenterEnabled)
        {
            _midiCueService.Connect();
        }

        // Wire up events
        _audioEngine.SectionChanged += AudioEngine_SectionChanged;
        _audioEngine.PlaybackStopped += AudioEngine_PlaybackStopped;

        // Initialize Presenter Integration
        _presenterSettingsService = new PresenterSettingsService(appDbPath);
        await _presenterSettingsService.LoadAsync();
        var presenterSettings = _presenterSettingsService.Settings;
        _presenterIntegration = new PresenterIntegration(_audioEngine, _midiCueService, _settings, presenterSettings, _presenterSettingsService, _songManager);
        _presenterIntegration.Initialize();
        _presenterIntegration.SongLoadRequested += PresenterIntegration_SongLoadRequested;
        _presenterIntegration.StatusMessage += PresenterIntegration_StatusMessage;

        // Populate display manager with actual monitor info
        PopulateDisplayManager(_presenterIntegration.DisplayManager);

        // F6/F7: clamp saved output/stage display indices to the monitors actually present now
        // (e.g. an index of 1 saved on a dual-monitor rig falls back to -1 on one monitor).
        _presenterIntegration.ClampDisplayIndicesToMonitors();

        // Start stage server if enabled
        _ = _presenterIntegration.StartStageServerAsync();

        // F1: auto-load the most recent service order so the sample service drives the app.
        // Missing stems from the first item degrade via LoadSong's try/catch — no startup crash.
        _ = _presenterIntegration.AutoLoadMostRecentServiceOrderAsync();

        // Auto-open the Presenter Operator Window on startup
        try
        {
            _presenterIntegration.OpenPresenterWindow();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Presenter auto-open failed: {ex.Message}");
        }

        // Populate song list
        RefreshSongList();

        // Load first song if available
        if (_songManager.Songs.Count > 0)
        {
            LoadSong(_songManager.Songs[0]);
        }

        _uiTimer.Start();
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _uiTimer.Stop();

        // Attempt graceful disposal with a hard timeout.
        // If anything hangs (Kestrel, WASAPI, MIDI), we force-exit.
        var disposeTask = Task.Run(() =>
        {
            try { _presenterIntegration?.Dispose(); } catch { }
            try { _audioEngine?.Dispose(); } catch { }
            try { _lightingService?.Dispose(); } catch { }
            try { _midiCueService?.Dispose(); } catch { }
        });

        // Give disposal max 2 seconds, then force-kill regardless
        disposeTask.Wait(TimeSpan.FromSeconds(2));

        // FORCE EXIT — ensures Kestrel background threads, WASAPI threads,
        // and any other non-daemon threads are terminated immediately.
        // This is the nuclear option but it's the ONLY reliable way to ensure
        // the process fully exits and doesn't lock DLLs for Visual Studio rebuild.
        Environment.Exit(0);
    }

    // ─── KEYBOARD SHORTCUTS ───────────────────────────────────────────────

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Space:
                TogglePlayPause();
                e.Handled = true;
                break;
            case Key.L:
                ToggleLoop();
                e.Handled = true;
                break;
            case Key.Right:
                NextSection();
                e.Handled = true;
                break;
            case Key.Left:
                PrevSection();
                e.Handled = true;
                break;
            case Key.Escape:
                StopPlayback();
                e.Handled = true;
                break;
            case Key.B:
                if (Keyboard.Modifiers == ModifierKeys.None)
                {
                    _lightingService?.ForceBlackout();
                    e.Handled = true;
                }
                break;
            // Number keys 1-9 jump to sections
            case Key.D1: case Key.D2: case Key.D3: case Key.D4: case Key.D5:
            case Key.D6: case Key.D7: case Key.D8: case Key.D9:
                int sectionNum = e.Key - Key.D1;
                JumpToSection(sectionNum);
                e.Handled = true;
                break;
        }
    }

    // ─── TRANSPORT CONTROLS ───────────────────────────────────────────────

    private void Play_Click(object sender, RoutedEventArgs e) => TogglePlayPause();
    private void Stop_Click(object sender, RoutedEventArgs e) => StopPlayback();
    private void PrevSection_Click(object sender, RoutedEventArgs e) => PrevSection();
    private void NextSection_Click(object sender, RoutedEventArgs e) => NextSection();
    private void Loop_Click(object sender, RoutedEventArgs e) => ToggleLoop();

    private void TogglePlayPause()
    {
        if (_audioEngine.IsPlaying)
        {
            _audioEngine.Pause();
            PlayBtn.Content = "PLAY";
        }
        else
        {
            _audioEngine.Play();
            _midiCueService?.OnPlaybackStarted();
            PlayBtn.Content = "PAUSE";
        }
    }

    private void StopPlayback()
    {
        _audioEngine.Stop();
        _midiCueService?.OnPlaybackStopped();
        PlayBtn.Content = "PLAY";
    }

    private void NextSection()
    {
        if (_currentSong == null) return;
        int next = _currentSectionIndex + 1;
        if (next < _currentSong.Sections.Count)
            JumpToSection(next);
    }

    private void PrevSection()
    {
        if (_currentSong == null) return;
        int prev = _currentSectionIndex - 1;
        if (prev >= 0)
            JumpToSection(prev);
    }

    private void JumpToSection(int index)
    {
        if (_currentSong == null || index < 0 || index >= _currentSong.Sections.Count)
            return;

        _audioEngine.JumpToSection(index);
        _currentSectionIndex = index;

        // Notify MIDI cue service of seek position
        var section = _currentSong.Sections[index];
        _midiCueService?.OnSeek(section.LoopInMs);

        UpdateSectionHighlight();
    }

    private void ToggleLoop()
    {
        if (_loopEnabled && _loopInfinite)
        {
            // Infinite → 1x
            _loopInfinite = false;
            _audioEngine.SetLoopInfinite(false);
            LoopStatusText.Text = "Loop: ON (1x)";
        }
        else if (_loopEnabled && !_loopInfinite)
        {
            // 1x → Off
            _loopEnabled = false;
            _audioEngine.SetLoopEnabled(false);
            LoopStatusText.Text = "Loop: OFF";
            LoopStatusText.Foreground = FindResource("TextMutedBrush") as SolidColorBrush;
        }
        else
        {
            // Off → Infinite
            _loopEnabled = true;
            _loopInfinite = true;
            _audioEngine.SetLoopEnabled(true);
            _audioEngine.SetLoopInfinite(true);
            LoopStatusText.Text = "Loop: ON (Infinite)";
            LoopStatusText.Foreground = FindResource("AccentGreenBrush") as SolidColorBrush;
        }
    }

    // ─── TEMPO & PITCH ────────────────────────────────────────────────────

    private void TempoDown_Click(object sender, RoutedEventArgs e)
    {
        _tempoPercent = Math.Max(50, _tempoPercent - 5);
        _audioEngine.SetTempo(_tempoPercent / 100.0);
        TempoText.Text = $"{_tempoPercent:F0}%";
        _presenterIntegration?.OnTempoChanged(_tempoPercent);
    }

    private void TempoUp_Click(object sender, RoutedEventArgs e)
    {
        _tempoPercent = Math.Min(150, _tempoPercent + 5);
        _audioEngine.SetTempo(_tempoPercent / 100.0);
        TempoText.Text = $"{_tempoPercent:F0}%";
        _presenterIntegration?.OnTempoChanged(_tempoPercent);
    }

    private void PitchDown_Click(object sender, RoutedEventArgs e)
    {
        _pitchSemitones = Math.Max(-6, _pitchSemitones - 1);
        _audioEngine.SetPitchShift(_pitchSemitones);
        UpdateKeyDisplay();
        _presenterIntegration?.OnPitchShiftChanged(_pitchSemitones);
    }

    private void PitchUp_Click(object sender, RoutedEventArgs e)
    {
        _pitchSemitones = Math.Min(6, _pitchSemitones + 1);
        _audioEngine.SetPitchShift(_pitchSemitones);
        UpdateKeyDisplay();
        _presenterIntegration?.OnPitchShiftChanged(_pitchSemitones);
    }

    private void UpdateKeyDisplay()
    {
        string originalKey = _currentSong?.OriginalKey ?? "C";
        int originalIndex = Array.IndexOf(_noteNames, originalKey);
        if (originalIndex < 0) originalIndex = 0;

        int newIndex = (originalIndex + _pitchSemitones + 12) % 12;
        string newKey = _noteNames[newIndex];
        string sign = _pitchSemitones > 0 ? "+" : "";
        KeyText.Text = $"{newKey} ({sign}{_pitchSemitones})";

        // Update chord chart window transposition (live, no data modification)
        if (_chordChartWindow != null)
        {
            _chordChartWindow.TransposeSemitones = _pitchSemitones;
            // Force refresh current slide to show transposed chords
            if (_chordChartWindow.IsVisible)
            {
                // Re-trigger slide display with new transposition
                _chordChartWindow.RefreshCurrentSlide();
            }
        }
    }

    // ─── SONG MANAGEMENT ──────────────────────────────────────────────────

    private async void AddSong_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select folder containing audio stems"
            };

            if (dialog.ShowDialog(this) == true)
            {
                string folderPath = dialog.FolderName;
                string title = Path.GetFileName(folderPath) ?? "New Song";

                Song song;

                // Check if song.json already exists (from previous analysis)
                var existingSongJson = Path.Combine(folderPath, "song.json");

                // Also check for single-track songs where song.json is in a subfolder
                if (!File.Exists(existingSongJson))
                {
                    // Look for song.json in a subfolder named after an audio file
                    var audioExts = new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a" };
                    var audioFiles = Directory.GetFiles(folderPath)
                        .Where(f => audioExts.Contains(Path.GetExtension(f).ToLower()))
                        .ToList();
                    if (audioFiles.Count == 1)
                    {
                        string stemFolder = Path.Combine(folderPath, Path.GetFileNameWithoutExtension(audioFiles[0]));
                        if (Directory.Exists(stemFolder))
                        {
                            var subJson = Path.Combine(stemFolder, "song.json");
                            if (File.Exists(subJson))
                            {
                                existingSongJson = subJson;
                                folderPath = stemFolder; // Point to stems folder
                            }
                        }
                    }
                }

                if (File.Exists(existingSongJson))
                {
                    // Load existing analysis — don't overwrite!
                    try
                    {
                        var json = await File.ReadAllTextAsync(existingSongJson);
                        var options = new System.Text.Json.JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true,
                            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
                        };
                        song = System.Text.Json.JsonSerializer.Deserialize<Song>(json, options) ?? new Song();
                        song.FolderPath = Path.GetDirectoryName(existingSongJson) ?? folderPath;

                        StatusText.Text = $"Loaded existing analysis: {song.Title} " +
                            $"({song.Sections.Count} sections, {song.Tracks.Count} tracks)";
                    }
                    catch
                    {
                        // If JSON is corrupt, fall back to creating new
                        song = _songManager.CreateSongFromFolder(folderPath, title);
                        await _songManager.SaveSongAsync(song);
                    }
                }
                else
                {
                    // No existing analysis — create new song from audio files
                    song = _songManager.CreateSongFromFolder(folderPath, title);
                    await _songManager.SaveSongAsync(song);
                }

                await _songManager.RegisterSongPathAsync(song.FolderPath);
                if (!_songManager.Songs.Contains(song))
                    _songManager.Songs.Add(song);

                RefreshSongList();
                LoadSong(song);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error adding song: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Remove the selected setlist song. Confirms first, stops playback if the song being
    /// removed is the one currently loaded, drops it from the registry so it won't reappear
    /// on restart, then refreshes the list and loads a sensible neighbour. Audio files on
    /// disk are left untouched.
    /// </summary>
    private async void DeleteSong_Click(object sender, RoutedEventArgs e)
    {
        // Resolve the selected song by IDENTITY (item Tag), not positional index.
        if (SetlistBox.SelectedItem is not ListBoxItem selItem || selItem.Tag is not Song song)
        {
            StatusText.Text = "Select a song in the setlist first, then click Delete Song.";
            return;
        }
        int index = _songManager.Songs.FindIndex(s => ReferenceEquals(s, song));
        if (index < 0) index = 0;

        // First confirmation.
        var confirm = MessageBox.Show(
            $"Delete \"{song.Title}\"?\n\n" +
            "This removes the song and ALL its data from the database (song record and play " +
            "history). The audio files and song.json on disk are kept.",
            "Delete Song",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm != MessageBoxResult.Yes) return;

        // Second confirmation — deleting SQL data is not undoable from the UI.
        var confirm2 = MessageBox.Show(
            $"Are you sure?\n\nAll database records for \"{song.Title}\" will be permanently " +
            "deleted. This cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm2 != MessageBoxResult.Yes) return;

        try
        {
            bool removingCurrent = ReferenceEquals(song, _currentSong);
            if (removingCurrent)
            {
                // Stop playback and release the audio engine's hold on this song before removal.
                StopPlayback();
                _audioEngine.UnloadSong();
            }

            await _songManager.RemoveSongAsync(song, deleteFiles: false);
            RefreshSongList();

            if (_songManager.Songs.Count == 0)
            {
                // Nothing left to show — clear the current song view.
                _currentSong = null;
                SongTitleText.Text = "No song loaded";
                SongInfoText.Text = "";
                SectionButtonsPanel.Children.Clear();
                SectionsPanel.Children.Clear();
                StatusText.Text = $"Deleted: {song.Title}. Setlist is empty.";
                return;
            }

            if (removingCurrent)
            {
                // Load the song that now occupies the removed slot (or the new last song).
                int nextIndex = Math.Min(index, _songManager.Songs.Count - 1);
                SetlistBox.SelectedIndex = nextIndex;
                LoadSong(_songManager.Songs[nextIndex]);
            }

            StatusText.Text = $"Deleted from database: {song.Title}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error removing song: {ex.Message}", "Error",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void EditSongInfo_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSong == null) return;

        var editor = new SongInfoEditorWindow(_currentSong);
        if (editor.ShowDialog() == true)
        {
            try
            {
                if (!string.IsNullOrEmpty(_currentSong.FolderPath))
                {
                    await _songManager.SaveSongAsync(_currentSong);
                    StatusText.Text = $"Song info saved: {_currentSong.Title}";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Update UI
            SongTitleText.Text = _currentSong.Title;
            SongInfoText.Text = $"{_currentSong.Bpm:F1} BPM | {_currentSong.OriginalKey} | {_currentSong.Tracks.Count} tracks | {_currentSong.Sections.Count} sections";
            RefreshSongList();
        }
    }

    /// <summary>
    /// Create Multi-Track: separate the selected single-track song into stems (Demucs via the
    /// Python analyzer), then AUTOMATICALLY generate a loud "Click (Auto)" track on the
    /// music-detected downbeat (kick/bass/chord phase). After it finishes, the song is reloaded so
    /// the new stems + click appear in the mixer. Does not touch section divisions or lyrics.
    /// </summary>
    private async void CreateMultiTrack_Click(object sender, RoutedEventArgs e)
    {
        // 1) Let the operator browse to the SINGLE mixed audio file to separate. The file's
        //    folder becomes the song folder; Demucs writes stems into a <folder>/<name>/ subfolder.
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select the single audio file to separate into stems",
            Filter = "Audio files (*.mp3;*.wav;*.flac;*.ogg;*.m4a)|*.mp3;*.wav;*.flac;*.ogg;*.m4a|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (picker.ShowDialog(this) != true)
            return; // cancelled

        string audioPath = picker.FileName;
        string songFolder = Path.GetDirectoryName(audioPath) ?? "";
        string songTitle = Path.GetFileNameWithoutExtension(audioPath);
        if (string.IsNullOrEmpty(songFolder) || !Directory.Exists(songFolder))
        {
            MessageBox.Show("Could not resolve the folder for the selected file.", "No Folder",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var confirm = MessageBox.Show(
            $"Separate \"{Path.GetFileName(audioPath)}\" into stems (vocals, drums, bass, guitar, " +
            "piano, other) using AI source separation, then auto-generate a loud click on the " +
            "detected downbeat?\n\nThis can take 1-10 minutes (much faster on a GPU).",
            "Create Multi-Track", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (confirm != MessageBoxResult.OK) return;

        // OPTIONAL tempo prompt: if the operator knows the real BPM (+ time signature), it LOCKS the
        // click grid to that tempo and finds beat 1 on a correct grid. Blank BPM = auto-detect.
        // Cancel here aborts Create Multi-Track entirely.
        var tempo = BpmPromptDialog.Show(this, songTitle);
        if (tempo == null) return; // cancelled
        double? userBpm = tempo.Value.Bpm;      // null = auto-detect
        int userTimeSig = tempo.Value.TimeSig;  // beats per bar (default 4)

        // Resolve the analysis script + python interpreter.
        string appFolder = AppDomain.CurrentDomain.BaseDirectory;
        string scriptPath = new[]
            {
                Path.Combine(appFolder, "Scripts", "analyze_multitrack.py"),
                Path.Combine(appFolder, "..", "Scripts", "analyze_multitrack.py"),
                Path.Combine(appFolder, "..", "..", "Scripts", "analyze_multitrack.py"),
                Path.Combine(appFolder, "..", "..", "..", "Scripts", "analyze_multitrack.py"),
            }.FirstOrDefault(File.Exists) ?? "";
        if (!File.Exists(scriptPath))
        {
            MessageBox.Show("Analysis script not found (Scripts/analyze_multitrack.py).",
                "Script Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        // Find or create the Song for this folder. If it isn't already in the setlist, create a
        // fresh one so Create Multi-Track also works as an import path (no pre-selection needed).
        Song song = _songManager.Songs.FirstOrDefault(s =>
                        SongPathResolver.PathsEqual(s.FolderPath, songFolder))
                    ?? new Song { Title = songTitle, FolderPath = songFolder };

        var btn = sender as Button;
        if (btn != null) btn.IsEnabled = false;
        ShowTaskProgress("Create Multi-Track", "Resolving Python interpreter…");
        UpdateTaskProgress(-1, ""); // indeterminate until the script reports progress

        try
        {
            StatusText.Text = "Resolving Python interpreter…";
            string configuredPath = _settings.PythonExecutablePath ?? "";
            var (chosenPath, _) = await Task.Run(() =>
            {
                var candidateOrder = PythonAnalysisService.BuildCandidateOrder();
                return PythonAnalysisService.ResolvePythonExecutable(
                    configuredPath, candidateOrder, PythonAnalysisService.ProbeCandidate);
            });
            if (string.IsNullOrEmpty(chosenPath))
            {
                MessageBox.Show(
                    "A working Python interpreter with librosa/demucs was not found.\n\n" +
                    "Set it in Edit Sections → 'Set Python…', then try again.",
                    "Python Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var service = new PythonAnalysisService(chosenPath, scriptPath);
            Action<int, string> onProgress = (pct, step) => Dispatcher.Invoke(() =>
            {
                UpdateTaskProgress(pct, step);
                StatusText.Text = string.IsNullOrWhiteSpace(step)
                    ? $"Create Multi-Track… {pct}%"
                    : $"Create Multi-Track… {pct}% — {step}";
            });

            // ── STEP 1: Demucs separation (runs the full analyzer on the single-track song). ──
            StatusText.Text = "Separating stems with Demucs (this can take a few minutes)…";
            UpdateTaskProgress(-1, "Separating stems with Demucs (this can take a few minutes)…");
            // skipDownbeatDetect: the dialog/DetectDownbeat step below is the SINGLE authoritative
            // downbeat+grid detection whose result drives the click, so don't correct the downbeat
            // here too (that was a redundant second detection that could disagree with the dialog).
            var (analyzed, analyzeMsg) = await service.AnalyzeSongAsync(
                songFolder, llmMode: "none", onProgress: onProgress, quality: "default",
                skipDownbeatDetect: true, bpmOverride: userBpm, timeSignature: userTimeSig);
            if (analyzed == null)
            {
                MessageBox.Show($"Stem separation failed:\n\n{analyzeMsg}",
                    "Separation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Demucs now writes the stems DIRECTLY into the original file's folder (alongside the
            // mix), and the analyzer saves song.json there too. So the song folder stays the
            // ORIGINAL folder — no subfolder repointing.
            string stemFolder = !string.IsNullOrEmpty(analyzed.FolderPath) && Directory.Exists(analyzed.FolderPath)
                ? analyzed.FolderPath
                : songFolder;

            song.Title = string.IsNullOrWhiteSpace(song.Title) ? songTitle : song.Title;
            song.FolderPath = stemFolder;
            song.Bpm = analyzed.Bpm;
            song.BeatIntervalMs = analyzed.BeatIntervalMs;
            song.BarDurationMs = analyzed.BarDurationMs;
            song.GridAnchorMs = analyzed.GridAnchorMs;
            song.OriginalKey = analyzed.OriginalKey;
            song.BeatPositionsMs = analyzed.BeatPositionsMs;
            if (analyzed.Tracks.Count > 0)
            {
                song.Tracks.Clear();
                foreach (var t in analyzed.Tracks) song.Tracks.Add(t);
            }

            // Leave the song SECTION-LESS on purpose. Section division is driven by the user's
            // provided lyrics (which already carry the exact section list/order) via Auto-Detect in
            // the Section Editor — an audio-only guess here would just be overwritten. Create
            // Multi-Track only prepares the audio: stems, corrected grid, and the click.
            song.Sections.Clear();
            song.ChordChart = null;

            // ── ANALYZE-FIRST: now the stems exist, run the fast downbeat/grid detection to get the
            // tempo analysis, show it, and let the operator choose the grid mode (Auto/Even/
            // Following). This reuses the SAME detection + dialog as the Section Editor button, so
            // both paths behave identically. If the operator picks a non-auto mode, we re-detect
            // with it (fast — stems already separated). The chosen grid's anchor/beats drive the
            // click below.
            double chosenAnchorMs = song.GridAnchorMs;
            double chosenTotalMs = analyzed.BeatPositionsMs != null && analyzed.BeatPositionsMs.Count > 0
                ? analyzed.BeatPositionsMs[^1] + song.BeatIntervalMs
                : EstimateSongDurationMs(stemFolder);
            try
            {
                UpdateTaskProgress(85, "Analyzing tempo (constant vs variable)…");
                var (det, _) = await service.DetectDownbeatAsync(stemFolder, onProgress,
                    gridMode: null, bpmOverride: userBpm, timeSignature: userTimeSig);
                if (det != null)
                {
                    var dbr = det.Value;
                    string? mode = GridModeDialog.Show(this, song.Title, dbr.GridFit, dbr.Bpm);
                    if (mode == null) { HideTaskProgress(); StatusText.Text = "Create Multi-Track cancelled."; return; }
                    if (mode != "auto" && !string.Equals(dbr.GridFit.Applied, mode, StringComparison.OrdinalIgnoreCase))
                    {
                        var (det2, _) = await service.DetectDownbeatAsync(stemFolder, onProgress,
                            gridMode: mode, bpmOverride: userBpm, timeSignature: userTimeSig);
                        if (det2 != null) dbr = det2.Value;
                    }
                    // Adopt the chosen grid, including its FINAL corrected beat positions so the
                    // click renders from exactly this grid (even or tempo-following as chosen).
                    chosenAnchorMs = dbr.CorrectedAnchorMs;
                    song.Bpm = dbr.Bpm;
                    song.BeatIntervalMs = dbr.BeatIntervalMs;
                    song.BarDurationMs = dbr.BarDurationMs;
                    song.GridAnchorMs = dbr.CorrectedAnchorMs;
                    if (dbr.CorrectedBeatsMs != null && dbr.CorrectedBeatsMs.Count >= 4)
                        song.BeatPositionsMs = dbr.CorrectedBeatsMs;
                    if (dbr.TotalDurationMs > 0) chosenTotalMs = dbr.TotalDurationMs;
                }
            }
            catch { /* non-fatal: fall back to the grid the full analysis already produced */ }

            // ── STEP 2: Generate the loud click on the chosen downbeat/grid. ──
            // Use the SHARED helper so this is identical to the Section Editor's "Auto Generate
            // Click Track" (single source of truth).
            UpdateTaskProgress(90, "Generating the click track on the detected downbeat…");
            string clickNote;
            try
            {
                double correctedAnchorMs = chosenAnchorMs;
                double totalMs = chosenTotalMs;
                var beatSvc = new WorshipPlayer.Audio.DSP.BeatDetectionService();
                beatSvc.ApplyAutoClickToSong(song, correctedAnchorMs, totalMs,
                    song.ManualClickCountInBeats, 44100);
                clickNote = $"Loud 'Click (Auto)' generated on the corrected downbeat " +
                            $"(beat 1 @ {correctedAnchorMs:F0} ms).";
            }
            catch (Exception clickEx)
            {
                clickNote = $"Stems created, but the click could not be generated ({clickEx.Message}). " +
                            "You can add one in Edit Sections → Auto Generate Click Track.";
            }

            // Persist, register, and ensure the song is in the setlist, then load it so the mixer
            // shows the new stems + click.
            UpdateTaskProgress(100, "Saving and loading the multitrack song…");
            await _songManager.SaveSongAsync(song);
            await _songManager.RegisterSongPathAsync(song.FolderPath);
            if (!_songManager.Songs.Any(s => SongPathResolver.PathsEqual(s.FolderPath, song.FolderPath)))
                _songManager.Songs.Add(song);
            _currentSong = song;
            RefreshSongList();
            LoadSong(song);
            HideTaskProgress();

            StatusText.Text = $"Multi-track created: {song.Tracks.Count} tracks. {clickNote}";
            MessageBox.Show(
                $"Created {song.Tracks.Count} tracks for \"{song.Title}\".\n\n" +
                $"{clickNote}\n\n" +
                "Next: open Edit Sections and run Auto-Detect with your lyrics to divide the song " +
                "into sections. The sections will land on the corrected downbeat so each loops cleanly.",
                "Create Multi-Track", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Create Multi-Track failed:\n\n{ex.Message}",
                "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = $"Create Multi-Track error: {ex.Message}";
        }
        finally
        {
            HideTaskProgress();
            if (btn != null) btn.IsEnabled = true;
        }
    }

    // ─── Long-task progress overlay helpers ───────────────────────────────
    private void ShowTaskProgress(string title, string step = "")
    {
        TaskProgressTitle.Text = title;
        TaskProgressStep.Text = step;
        TaskProgressBar.IsIndeterminate = false;
        TaskProgressBar.Value = 0;
        TaskProgressPercent.Text = "";
        TaskProgressOverlay.Visibility = Visibility.Visible;
    }

    private void UpdateTaskProgress(int percent, string step)
    {
        if (percent < 0)
        {
            TaskProgressBar.IsIndeterminate = true;
            TaskProgressPercent.Text = "";
        }
        else
        {
            TaskProgressBar.IsIndeterminate = false;
            TaskProgressBar.Value = Math.Clamp(percent, 0, 100);
            TaskProgressPercent.Text = $"{Math.Clamp(percent, 0, 100)}%";
        }
        if (!string.IsNullOrWhiteSpace(step)) TaskProgressStep.Text = step;
    }

    private void HideTaskProgress()
    {
        TaskProgressOverlay.Visibility = Visibility.Collapsed;
    }

    /// <summary>Best-effort duration (ms) of the first audio file in a folder, for grid building.</summary>
    private static double EstimateSongDurationMs(string folder)
    {
        try
        {
            var audioExts = new[] { ".mp3", ".wav", ".flac", ".ogg", ".m4a" };
            var file = Directory.GetFiles(folder)
                .FirstOrDefault(f => audioExts.Contains(Path.GetExtension(f).ToLower()));
            if (file == null) return 0;
            using var reader = new NAudio.Wave.AudioFileReader(file);
            return reader.TotalTime.TotalMilliseconds;
        }
        catch { return 0; }
    }

    private async void EditSections_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSong == null) return;
        var editor = new SectionEditorWindow(_currentSong, _audioEngine,
            _settings.PythonExecutablePath,
            async value =>
            {
                _settings.PythonExecutablePath = value;
                await _settingsService.SaveAsync();
            });

        // Feed the global auto-slide config (lines/slide + lead ms) from the current
        // PresenterSettings into the editor so the slide-creation pipeline uses the
        // user's configured values instead of always defaulting to 4/500. Settings is
        // always non-null and defaults to 4/500, so an untouched config behaves as before.
        editor.LinesPerSlide = _presenterSettingsService.Settings.LinesPerSlide;
        editor.SlideLeadMs = _presenterSettingsService.Settings.SlideLeadMs;

        // Feed the slide-label catalog (đợt 2 T8 / R5.3) so the editor's Label dropdown can
        // assign Section.LabelId. A pre-labels settings file (null) falls back to the
        // seeded defaults, matching T7's EnsureLabels semantics for display purposes.
        editor.SlideLabels = _presenterSettingsService.Settings.SlideLabels
            ?? WorshipPlayer.Core.Services.SlideLabelService.CreateDefaults();

        if (editor.ShowDialog() == true)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentSong.FolderPath))
                {
                    MessageBox.Show("Cannot save: Song folder path is not set.\n" +
                        "Please add the song via '+ Add Song' first.",
                        "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                await _songManager.SaveSongAsync(_currentSong);
                BuildSectionButtons();
                StatusText.Text = $"Saved: {_currentSong.Title} ({_currentSong.Sections.Count} sections)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving song:\n{ex.Message}",
                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // Save as AI training data (user confirmed sections = training signal)
            try
            {
                await _trainingDataService.SaveTrainingDataAsync(
                    _currentSong, wasCueTrackUsed: false, wasManuallyEdited: true);
            }
            catch { } // Non-critical — don't block user
        }
    }

    private void Lighting_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSong == null) return;
        var window = new LightingWindow(_currentSong, _lightingService, _settings);
        window.ShowDialog();
    }

    private void MidiCues_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSong == null) return;

        var editor = new MidiCueEditorWindow(
            _currentSong,
            _midiCueService,
            () => _audioEngine?.GetPositionMs() ?? 0);

        if (editor.ShowDialog() == true)
        {
            // Cues were saved — persist song.json
            try
            {
                _ = _songManager.SaveSongAsync(_currentSong);
                StatusText.Text = $"MIDI cues saved ({_currentSong.MidiCues.Count} cues)";
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Error saving: {ex.Message}";
            }
        }
    }

    private void OpenPresenter_Click(object sender, RoutedEventArgs e)
    {
        _presenterIntegration?.OpenPresenterWindow();
    }

    private void PresenterIntegration_SongLoadRequested(object? sender, string songFolder)
    {
        if (string.IsNullOrWhiteSpace(songFolder)) return;

        // Resolve by IDENTITY, most specific first, so we never load the wrong song:
        //   1. exact full-path equality (normalized),
        //   2. exact folder-NAME equality (handles a request that passes just the leaf folder).
        // The old code used FolderPath.EndsWith(songFolder), which could match a DIFFERENT song
        // whose path merely ended with the requested string (e.g. a short/partial name) — that was
        // a cause of "played a different song".
        var song = _songManager.Songs.FirstOrDefault(s =>
                       SongPathResolver.PathsEqual(s.FolderPath, songFolder))
                   ?? _songManager.Songs.FirstOrDefault(s =>
                       string.Equals(Path.GetFileName(s.FolderPath), Path.GetFileName(songFolder),
                           StringComparison.OrdinalIgnoreCase));

        if (song != null)
        {
            Dispatcher.Invoke(() => LoadSong(song));
        }
    }

    private void PresenterIntegration_StatusMessage(object? sender, string message)
    {
        // Route non-fatal presenter status messages to the status bar (best-effort).
        Dispatcher.Invoke(() => StatusText.Text = message);
    }

    private ChordChartWindow? _chordChartWindow;

    private void ChordChart_Click(object sender, RoutedEventArgs e)
    {
        if (_currentSong == null) return;

        if (_currentSong.ChordChart == null || _currentSong.ChordChart.Slides.Count == 0)
        {
            MessageBox.Show(
                "No chord chart data available for this song.\n\n" +
                "Run Auto-Detect in the Section Editor with Whisper installed\n" +
                "to generate lyrics and chord chart data.\n\n" +
                "Required: pip install openai-whisper",
                "No Chord Chart", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        try
        {
            // Close existing window if open
            if (_chordChartWindow != null)
            {
                try { _chordChartWindow.Close(); } catch { }
            }

            _chordChartWindow = new ChordChartWindow();
            _chordChartWindow.SetData(_currentSong, _currentSong.ChordChart,
                () => _audioEngine?.GetPositionMs() ?? 0);
            _chordChartWindow.Closed += (s, ev) => _chordChartWindow = null;
            _chordChartWindow.Show();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error opening chord chart:\n{ex.Message}\n\n{ex.StackTrace}",
                "Chord Chart Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void SetlistBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Ignore selection events raised while RefreshSongList is rebuilding the list (clearing
        // items transiently fires SelectionChanged with a stale index).
        if (_suppressSetlistSelection) return;

        // Resolve the song by IDENTITY (the item's Tag), not by positional index. Positional
        // indexing broke when the in-memory Songs order and the rendered rows diverged, which
        // could load/play the wrong song.
        if (SetlistBox.SelectedItem is ListBoxItem item && item.Tag is Song song)
        {
            LoadSong(song);
        }
    }

    private async void LoadSong(Song song)
    {
        // Claim the newest load token. Any load already in flight becomes stale and must not
        // activate its mixer when it finishes.
        int myGeneration = ++_loadGeneration;

        _currentSong = song;
        _currentSectionIndex = 0;
        _pitchSemitones = 0;
        _tempoPercent = 100.0;

        // Load MIDI cues for this song
        _midiCueService?.LoadSong(song);

        // Update UI
        SongTitleText.Text = song.Title;
        SongInfoText.Text = $"{song.Bpm:F1} BPM | {song.OriginalKey} | {song.Tracks.Count} tracks | {song.Sections.Count} sections";
        TempoText.Text = "100%";
        UpdateKeyDisplay();

        // Build UI (do this BEFORE audio loading so sections show even if audio fails)
        BuildSectionButtons();
        BuildChannelStrips();

        // Debug: show section count
        if (song.Sections.Count == 0)
        {
            StatusText.Text = $"WARNING: No sections found in song.json for '{song.Title}' at {song.FolderPath}";
        }

        // Load into audio engine (preloads all tracks into memory)
        try
        {
            StatusText.Text = $"Loading '{song.Title}' into memory...";

            // Force UI update before heavy loading
            Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);

            // Phase 1: Decode audio files on background thread (heavy, no WASAPI)
            var mixer = await Task.Run(() => _audioEngine.PreloadSong(song));

            // If a newer LoadSong started while we were decoding, this result is stale — discard it
            // so we never activate the wrong song's mixer over the one the user actually chose.
            if (myGeneration != _loadGeneration)
            {
                mixer.Dispose();
                return;
            }

            // Phase 2: Initialize WASAPI on UI thread (light, instant)
            _audioEngine.ActivateSong(mixer);

            _audioEngine.SetLoopEnabled(_loopEnabled);
            _audioEngine.SetLoopInfinite(_loopInfinite);

            // If NO track files resolved, the mixer is silent. Surface this loudly instead of
            // leaving the operator to press Play and hear nothing (or confuse it with a prior song).
            if (mixer.TrackCount == 0)
            {
                StatusText.Text = $"⚠ Loaded '{song.Title}' but 0 of {song.Tracks.Count} track files were " +
                    $"found on disk at: {song.FolderPath} — nothing will play. Check the song folder/tracks.";
                MessageBox.Show(
                    $"\"{song.Title}\" has no playable audio: none of its {song.Tracks.Count} track file(s) " +
                    $"were found on disk.\n\nFolder: {song.FolderPath}\n\n" +
                    "The song's info shows, but pressing Play will produce no sound. Re-add the song or " +
                    "run Create Multi-Track / Auto-Detect so the audio files resolve.",
                    "No Playable Audio", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                StatusText.Text = $"Loaded: {song.Title} ({mixer.TrackCount}/{song.Tracks.Count} tracks in memory) | [SPACE]=Play [L]=Loop [Arrows]=Sections [1-9]=Jump";
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Audio load error: {ex.Message} — Make sure MP3/WAV files are in the Songs folder";
        }

        // Set lighting
        _lightingService.SetSong(song);

        // Notify presenter
        _presenterIntegration?.OnSongLoaded(song);
    }

    private void RefreshSongList()
    {
        // Suppress selection events while rebuilding; clearing items fires spurious
        // SelectionChanged with a stale index that must not trigger a load.
        _suppressSetlistSelection = true;
        try
        {
            SetlistBox.Items.Clear();
            foreach (var song in _songManager.Songs)
            {
                SetlistBox.Items.Add(new ListBoxItem
                {
                    Content = $"{song.Title}",
                    Tag = song, // identity — selection resolves the song from this, not the index
                    Foreground = new SolidColorBrush(Colors.White),
                    FontSize = 14,
                    Padding = new Thickness(8, 6, 8, 6)
                });
            }

            // Re-select the current song by identity so the highlight matches what's loaded.
            if (_currentSong != null)
            {
                foreach (ListBoxItem it in SetlistBox.Items)
                {
                    if (it.Tag is Song s && SongPathResolver.PathsEqual(s.FolderPath, _currentSong.FolderPath))
                    {
                        SetlistBox.SelectedItem = it;
                        break;
                    }
                }
            }
        }
        finally
        {
            _suppressSetlistSelection = false;
        }
    }

    // ─── BUILD UI ELEMENTS ────────────────────────────────────────────────

    private void BuildSectionButtons()
    {
        SectionButtonsPanel.Children.Clear();
        SectionsPanel.Children.Clear();
        if (_currentSong == null) return;

        for (int i = 0; i < _currentSong.Sections.Count; i++)
        {
            var section = _currentSong.Sections[i];
            int index = i;

            // Left panel section button (iSingWorship style)
            var sectionBtn = new Border
            {
                Background = FindResource("PrimaryDarkBrush") as SolidColorBrush,
                BorderBrush = new SolidColorBrush(Colors.Transparent),
                BorderThickness = new Thickness(3, 0, 0, 0),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(2, 2, 2, 2),
                Padding = new Thickness(10, 8, 10, 8),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = index
            };

            var btnContent = new StackPanel();
            var nameText = new TextBlock
            {
                Text = $"{i + 1}. {section.Name}",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = FindResource("TextPrimaryBrush") as SolidColorBrush
            };
            var infoText = new TextBlock
            {
                Text = $"Bars {section.StartBar}-{section.EndBar} | {MsToTimecode(section.DurationMs)}",
                FontSize = 10,
                Foreground = FindResource("TextMutedBrush") as SolidColorBrush
            };
            var loopIndicator = new TextBlock
            {
                Text = "",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = FindResource("AccentGreenBrush") as SolidColorBrush,
                Name = "LoopIndicator"
            };

            // Section progress bar (shows remaining time in current section)
            var sectionProgressBar = new ProgressBar
            {
                Height = 4,
                Minimum = 0,
                Maximum = 100,
                Value = 0,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(section.Color ?? "#4A90D9")),
                Background = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 4, 0, 0),
                Visibility = Visibility.Collapsed,
                Tag = $"progress_{index}"
            };

            btnContent.Children.Add(nameText);
            btnContent.Children.Add(infoText);
            btnContent.Children.Add(loopIndicator);
            btnContent.Children.Add(sectionProgressBar);
            sectionBtn.Child = btnContent;

            sectionBtn.MouseLeftButtonDown += (s, ev) => SectionButton_Click(index);
            sectionBtn.MouseEnter += (s, ev) =>
            {
                if ((int)((Border)s).Tag != _currentSectionIndex)
                    ((Border)s).Background = FindResource("SurfaceDarkBrush") as SolidColorBrush;
            };
            sectionBtn.MouseLeave += (s, ev) =>
            {
                if ((int)((Border)s).Tag != _currentSectionIndex)
                    ((Border)s).Background = FindResource("PrimaryDarkBrush") as SolidColorBrush;
            };

            SectionButtonsPanel.Children.Add(sectionBtn);

            // Top section bar buttons (smaller, for quick reference)
            var topBtn = new Button
            {
                Content = $"{i + 1}. {section.Name}",
                Style = FindResource("SectionButton") as Style,
                Tag = index
            };
            topBtn.Click += (s, e) => SectionButton_Click(index);
            SectionsPanel.Children.Add(topBtn);
        }

        UpdateSectionHighlight();
    }

    /// <summary>
    /// New playback behavior:
    /// - Single-click on current section: queue restart (finish current, replay once, then advance)
    /// - Double-click on current section: infinite loop
    /// - Single-click on different section while playing: queue it (plays after current finishes)
    /// - Single-click on different section while stopped: jump immediately and play
    /// </summary>
    private DateTime _lastSectionClickTime = DateTime.MinValue;
    private int _lastSectionClickIndex = -1;
    private DispatcherTimer? _singleClickTimer;
    private int _pendingClickIndex = -1;

    private void SectionButton_Click(int index)
    {
        if (_currentSong == null || index < 0 || index >= _currentSong.Sections.Count)
            return;

        DateTime now = DateTime.Now;
        double msSinceLastClick = (now - _lastSectionClickTime).TotalMilliseconds;
        bool isDoubleClick = (msSinceLastClick < 400 && _lastSectionClickIndex == index);

        _lastSectionClickTime = now;
        _lastSectionClickIndex = index;

        if (isDoubleClick)
        {
            // Cancel any pending single-click action
            _singleClickTimer?.Stop();
            _pendingClickIndex = -1;

            // Check if section allows looping (skipped sections cannot loop)
            if (_currentSong.Sections[index].CanLoop == false)
            {
                // Section was marked as skip — just play once, no loop
                LoopStatusText.Text = "Cannot loop (skipped section)";
                LoopStatusText.Foreground = FindResource("TextMutedBrush") as SolidColorBrush;
                UpdateSectionHighlight();
                return;
            }

            // DOUBLE-CLICK → infinite loop on this section
            _loopInfinite = true;
            _loopEnabled = true;
            _audioEngine.SetLoopEnabled(true);
            _audioEngine.SetLoopInfinite(true);
            _audioEngine.ClearQueue();

            // Jump to this section if not already there
            if (index != _currentSectionIndex || !_audioEngine.IsPlaying)
            {
                _audioEngine.JumpToSection(index);
                _currentSectionIndex = index;
            }

            // Auto-play if stopped
            if (!_audioEngine.IsPlaying)
            {
                _audioEngine.Play();
                PlayBtn.Content = "PAUSE";
            }

            LoopStatusText.Text = "Loop: INFINITE";
            LoopStatusText.Foreground = FindResource("AccentGreenBrush") as SolidColorBrush;
            UpdateSectionHighlight();
            return;
        }

        // Defer single-click action to allow double-click detection
        _pendingClickIndex = index;
        if (_singleClickTimer == null)
        {
            _singleClickTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(420)
            };
            _singleClickTimer.Tick += SingleClickTimer_Tick;
        }
        _singleClickTimer.Stop();
        _singleClickTimer.Start();
    }

    private void SingleClickTimer_Tick(object? sender, EventArgs e)
    {
        _singleClickTimer?.Stop();
        int index = _pendingClickIndex;
        _pendingClickIndex = -1;

        if (_currentSong == null || index < 0 || index >= _currentSong.Sections.Count)
            return;

        if (index == _currentSectionIndex && _audioEngine.IsPlaying)
        {
            // SINGLE-CLICK on current section WHILE PLAYING → queue restart (finish current, replay once, then advance)
            _loopInfinite = false;
            _loopEnabled = false;
            _audioEngine.SetLoopEnabled(false);
            _audioEngine.SetLoopInfinite(false);
            _audioEngine.QueueSection(index); // Queue same section = restart after finishing
            LoopStatusText.Text = "Restart → then next";
            LoopStatusText.Foreground = FindResource("AccentOrangeBrush") as SolidColorBrush;
        }
        else if (index == _currentSectionIndex && !_audioEngine.IsPlaying)
        {
            // SINGLE-CLICK on current section WHILE STOPPED → just play it
            _loopInfinite = false;
            _loopEnabled = false;
            _audioEngine.SetLoopEnabled(false);
            _audioEngine.SetLoopInfinite(false);
            _audioEngine.ClearQueue();
            _audioEngine.JumpToSection(index);
            _audioEngine.Play();
            PlayBtn.Content = "PAUSE";
            LoopStatusText.Text = "Play → next";
            LoopStatusText.Foreground = FindResource("AccentOrangeBrush") as SolidColorBrush;
        }
        else if (_audioEngine.IsPlaying)
        {
            // SINGLE-CLICK on different section while playing → queue it
            _audioEngine.QueueSection(index);
            // Disable loop so current section will finish and advance to queued
            _loopInfinite = false;
            _loopEnabled = false;
            _audioEngine.SetLoopEnabled(false);
            _audioEngine.SetLoopInfinite(false);
            LoopStatusText.Text = $"Next: {_currentSong.Sections[index].Name}";
            LoopStatusText.Foreground = FindResource("AccentOrangeBrush") as SolidColorBrush;
        }
        else
        {
            // SINGLE-CLICK on different section while stopped → jump immediately
            _loopInfinite = false;
            _loopEnabled = false;
            _audioEngine.SetLoopEnabled(false);
            _audioEngine.SetLoopInfinite(false);
            _audioEngine.ClearQueue();
            _audioEngine.JumpToSection(index);
            _currentSectionIndex = index;
            LoopStatusText.Text = "Play → next";
            LoopStatusText.Foreground = FindResource("AccentOrangeBrush") as SolidColorBrush;

            // Start playing
            _audioEngine.Play();
            PlayBtn.Content = "PAUSE";
        }

        UpdateSectionHighlight();
    }

    private void BuildChannelStrips()
    {
        ChannelStripsPanel.Children.Clear();
        if (_currentSong == null) return;

        for (int i = 0; i < _currentSong.Tracks.Count; i++)
        {
            var trackInfo = _currentSong.Tracks[i];
            int index = i;

            var strip = CreateChannelStrip(trackInfo, index);
            ChannelStripsPanel.Children.Add(strip);
        }
    }

    private Border CreateChannelStrip(TrackInfo trackInfo, int index)
    {
        var strip = new Border
        {
            Background = FindResource("SecondaryDarkBrush") as SolidColorBrush,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8),
            Margin = new Thickness(4),
            MinWidth = 90
        };

        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };

        // Track label
        var label = new TextBlock
        {
            Text = trackInfo.Label,
            FontSize = 11,
            FontWeight = FontWeights.Bold,
            Foreground = FindResource("TextPrimaryBrush") as SolidColorBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = System.Windows.TextAlignment.Center
        };
        panel.Children.Add(label);

        // Output indicator (clickable — toggles FOH → IEM → Both)
        var outputBtn = new Button
        {
            Content = trackInfo.Output.ToString(),
            FontSize = 9,
            FontWeight = FontWeights.SemiBold,
            Foreground = trackInfo.Output == TrackOutput.IEM
                ? FindResource("AccentOrangeBrush") as SolidColorBrush
                : trackInfo.Output == TrackOutput.Both
                    ? new SolidColorBrush(Color.FromRgb(80, 227, 194))  // Teal for Both
                    : FindResource("TextMutedBrush") as SolidColorBrush,
            Background = new SolidColorBrush(Color.FromRgb(30, 30, 46)),
            BorderBrush = trackInfo.Output == TrackOutput.IEM
                ? FindResource("AccentOrangeBrush") as SolidColorBrush
                : new SolidColorBrush(Color.FromRgb(74, 74, 96)),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(6, 1, 6, 1),
            Margin = new Thickness(0, 2, 0, 8),
            Cursor = Cursors.Hand,
            ToolTip = "Click to toggle output: FOH (speakers) → IEM (in-ear) → Both"
        };
        int trackIdx = index;
        outputBtn.Click += (s, ev) =>
        {
            // Cycle: FOH → IEM → Both → FOH
            var currentOutput = trackInfo.Output;
            var newOutput = currentOutput switch
            {
                TrackOutput.FOH => TrackOutput.IEM,
                TrackOutput.IEM => TrackOutput.Both,
                TrackOutput.Both => TrackOutput.FOH,
                _ => TrackOutput.FOH
            };
            trackInfo.Output = newOutput;
            outputBtn.Content = newOutput.ToString();

            // Update colors
            outputBtn.Foreground = newOutput == TrackOutput.IEM
                ? FindResource("AccentOrangeBrush") as SolidColorBrush
                : newOutput == TrackOutput.Both
                    ? new SolidColorBrush(Color.FromRgb(80, 227, 194))
                    : FindResource("TextMutedBrush") as SolidColorBrush;
            outputBtn.BorderBrush = newOutput == TrackOutput.IEM
                ? FindResource("AccentOrangeBrush") as SolidColorBrush
                : new SolidColorBrush(Color.FromRgb(74, 74, 96));
        };
        panel.Children.Add(outputBtn);

        // Volume slider
        var slider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = trackInfo.DefaultVolume * 100,
            Orientation = Orientation.Vertical,
            Height = 120,
            Width = 24,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        slider.ValueChanged += (s, e) =>
        {
            _audioEngine.SetTrackVolume(index, (float)(e.NewValue / 100.0));
        };
        panel.Children.Add(slider);

        // Mute button
        var muteBtn = new Button
        {
            Content = "M",
            Width = 36,
            Height = 28,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 6, 0, 2),
            Background = trackInfo.DefaultMute
                ? FindResource("AccentRedBrush") as SolidColorBrush
                : FindResource("SurfaceDarkBrush") as SolidColorBrush,
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0)
        };
        muteBtn.Click += (s, e) =>
        {
            var btn = s as Button;
            bool isMuted = btn!.Background == FindResource("AccentRedBrush") as SolidColorBrush;
            isMuted = !isMuted;
            btn.Background = isMuted
                ? FindResource("AccentRedBrush") as SolidColorBrush
                : FindResource("SurfaceDarkBrush") as SolidColorBrush;
            _audioEngine.SetTrackMute(index, isMuted);
        };
        panel.Children.Add(muteBtn);

        // Solo button
        var soloBtn = new Button
        {
            Content = "S",
            Width = 36,
            Height = 28,
            FontSize = 12,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 2, 0, 0),
            Background = FindResource("SurfaceDarkBrush") as SolidColorBrush,
            Foreground = new SolidColorBrush(Colors.White),
            BorderThickness = new Thickness(0)
        };
        soloBtn.Click += (s, e) =>
        {
            var btn = s as Button;
            bool isSolo = btn!.Background == FindResource("AccentGreenBrush") as SolidColorBrush;
            isSolo = !isSolo;
            btn.Background = isSolo
                ? FindResource("AccentGreenBrush") as SolidColorBrush
                : FindResource("SurfaceDarkBrush") as SolidColorBrush;
            _audioEngine.SetTrackSolo(index, isSolo);
        };
        panel.Children.Add(soloBtn);

        strip.Child = panel;
        return strip;
    }

    private void UpdateSectionHighlight()
    {
        // Update left panel section buttons
        for (int i = 0; i < SectionButtonsPanel.Children.Count; i++)
        {
            if (SectionButtonsPanel.Children[i] is Border border)
            {
                var stack = border.Child as StackPanel;
                var loopIndicator = stack?.Children.Count > 2 ? stack.Children[2] as TextBlock : null;

                if (i == _currentSectionIndex)
                {
                    border.Background = FindResource("SurfaceDarkBrush") as SolidColorBrush;
                    border.BorderBrush = FindResource("AccentGreenBrush") as SolidColorBrush;

                    if (loopIndicator != null)
                    {
                        loopIndicator.Text = _loopInfinite ? ">> LOOPING" : "> PLAY ONCE";
                        loopIndicator.Foreground = _loopInfinite
                            ? FindResource("AccentGreenBrush") as SolidColorBrush
                            : FindResource("AccentOrangeBrush") as SolidColorBrush;
                    }
                }
                else
                {
                    border.Background = FindResource("PrimaryDarkBrush") as SolidColorBrush;
                    border.BorderBrush = new SolidColorBrush(Colors.Transparent);

                    if (loopIndicator != null)
                        loopIndicator.Text = "";
                }
            }
        }

        // Update top section bar buttons
        for (int i = 0; i < SectionsPanel.Children.Count; i++)
        {
            if (SectionsPanel.Children[i] is Button btn)
            {
                if (i == _currentSectionIndex)
                {
                    btn.Background = FindResource("AccentBlueBrush") as SolidColorBrush;
                    btn.BorderBrush = FindResource("AccentGreenBrush") as SolidColorBrush;
                }
                else
                {
                    btn.Background = FindResource("SecondaryDarkBrush") as SolidColorBrush;
                    btn.BorderBrush = new SolidColorBrush(Colors.Transparent);
                }
            }
        }

        // Update current section text
        if (_currentSong != null && _currentSectionIndex >= 0 && _currentSectionIndex < _currentSong.Sections.Count)
        {
            var section = _currentSong.Sections[_currentSectionIndex];
            string loopIcon = _loopInfinite ? "∞" : "1x";
            CurrentSectionText.Text = $"  [{section.Name}] ({loopIcon})";
        }
    }

    // ─── EVENTS ───────────────────────────────────────────────────────────

    private void AudioEngine_SectionChanged(object? sender, SectionChangedEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            _currentSectionIndex = e.NewSectionIndex;
            UpdateSectionHighlight();
            CurrentSectionText.Text = $"  [{e.NewSection?.Name ?? "?"}]";

            // Trigger lighting
            _lightingService.OnSectionChanged(e.NewSectionIndex);
        });
    }

    private void AudioEngine_PlaybackStopped(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            PlayBtn.Content = "PLAY";

            // Send "Next Item" to WorshipTools Presenter when song ends
            _midiCueService?.OnSongEnded();
            _midiCueService?.OnPlaybackStopped();
        });
    }

    private void UITimer_Tick(object? sender, EventArgs e)
    {
        if (_audioEngine == null || !_audioEngine.IsPlaying) return;

        double ms = _audioEngine.GetPositionMs();
        PositionText.Text = MsToTimecode(ms);

        // Update MIDI cue service with current position
        _midiCueService?.UpdatePosition(ms);

        // Update section progress bars and queued section indicator
        UpdateSectionProgress(ms);
    }

    /// <summary>
    /// Update progress bars on section buttons and flashing indicator for queued section.
    /// </summary>
    private void UpdateSectionProgress(double currentMs)
    {
        if (_currentSong == null) return;

        // Always sync _currentSectionIndex from the audio engine (source of truth)
        int engineSectionIndex = _audioEngine.CurrentSectionIndex;
        if (engineSectionIndex >= 0 && engineSectionIndex != _currentSectionIndex)
        {
            _currentSectionIndex = engineSectionIndex;
            UpdateSectionHighlight();
        }

        int queuedIndex = _audioEngine.QueuedSectionIndex;

        for (int i = 0; i < SectionButtonsPanel.Children.Count; i++)
        {
            if (SectionButtonsPanel.Children[i] is not Border border) continue;
            if (border.Child is not StackPanel panel) continue;

            // Find the progress bar in this button's panel
            ProgressBar? progressBar = null;
            TextBlock? loopIndicator = null;
            foreach (var child in panel.Children)
            {
                if (child is ProgressBar pb) progressBar = pb;
                if (child is TextBlock tb && tb.Name == "LoopIndicator") loopIndicator = tb;
            }

            if (i == _currentSectionIndex && i < _currentSong.Sections.Count)
            {
                // Current section: show and update progress bar
                var section = _currentSong.Sections[i];
                double sectionDuration = section.LoopOutMs - section.LoopInMs;
                double sectionElapsed = currentMs - section.LoopInMs;

                if (sectionDuration > 0 && sectionElapsed >= 0)
                {
                    double progress = Math.Clamp((sectionElapsed / sectionDuration) * 100, 0, 100);
                    if (progressBar != null)
                    {
                        progressBar.Value = progress;
                        progressBar.Visibility = Visibility.Visible;
                    }
                }

                // Show loop indicator if in infinite loop mode
                if (loopIndicator != null)
                {
                    loopIndicator.Text = _loopInfinite ? "LOOP" : "";
                }
            }
            else
            {
                // Not current section: hide progress bar
                if (progressBar != null)
                {
                    progressBar.Value = 0;
                    progressBar.Visibility = Visibility.Collapsed;
                }
                if (loopIndicator != null)
                {
                    loopIndicator.Text = "";
                }
            }

            // Queued section: show flashing "UP NEXT" indicator
            if (i == queuedIndex)
            {
                // Flash by toggling opacity based on time
                bool flashOn = (DateTime.Now.Millisecond / 500) % 2 == 0;
                border.BorderBrush = flashOn
                    ? FindResource("AccentOrangeBrush") as SolidColorBrush
                    : new SolidColorBrush(Colors.Transparent);
                border.BorderThickness = new Thickness(3, 0, 0, 0);

                if (loopIndicator != null)
                {
                    loopIndicator.Text = "UP NEXT";
                    loopIndicator.Foreground = FindResource("AccentOrangeBrush") as SolidColorBrush;
                }
            }
            else if (i != _currentSectionIndex)
            {
                // Reset border for non-queued, non-current sections
                border.BorderBrush = new SolidColorBrush(Colors.Transparent);
            }
        }
    }

    /// <summary>
    /// Convert milliseconds to timecode format M:SS:MMM
    /// </summary>
    private static string MsToTimecode(double ms)
    {
        int totalMs = (int)ms;
        int minutes = totalMs / 60000;
        int seconds = (totalMs % 60000) / 1000;
        int millis = totalMs % 1000;
        return $"{minutes}:{seconds:00}:{millis:000}";
    }

    private void UpdateOscStatus()
    {
        if (_settings.OscEnabled && _lightingService?.OscController?.IsConnected == true)
            OscStatusText.Text = $"OSC: {_settings.OscHost}:{_settings.OscPort}";
        else if (_settings.OscEnabled)
            OscStatusText.Text = "OSC: Connecting...";
        else
            OscStatusText.Text = "OSC: Disabled";
    }

    /// <summary>
    /// Find a folder by checking multiple locations (app dir, parent, working dir).
    /// </summary>
    private static string FindFolder(string folderName, string appFolder)
    {
        // 1. Check next to the exe
        var path1 = Path.Combine(appFolder, folderName);
        if (Directory.Exists(path1)) return path1;

        // 2. Check parent of app folder (for dev/debug scenarios)
        var parent = Directory.GetParent(appFolder)?.FullName ?? appFolder;
        var path2 = Path.Combine(parent, folderName);
        if (Directory.Exists(path2)) return path2;

        // 3. Check working directory
        var path3 = Path.Combine(Directory.GetCurrentDirectory(), folderName);
        if (Directory.Exists(path3)) return path3;

        // 4. Check up to 3 levels up (for nested build output)
        var current = appFolder;
        for (int i = 0; i < 4; i++)
        {
            var check = Path.Combine(current, folderName);
            if (Directory.Exists(check)) return check;
            var parentDir = Directory.GetParent(current);
            if (parentDir == null) break;
            current = parentDir.FullName;
        }

        // 5. Fallback: create next to exe and populate with default song
        Directory.CreateDirectory(path1);
        if (folderName == "Songs")
        {
            CreateDefaultSong(path1);
        }
        return path1;
    }

    /// <summary>
    /// Create the default "Open The Heavens" song.json if Songs folder is empty.
    /// </summary>
    private static void CreateDefaultSong(string songsFolder)
    {
        var songDir = Path.Combine(songsFolder, "Open The Heavens");
        Directory.CreateDirectory(songDir);
        var songJsonPath = Path.Combine(songDir, "song.json");

        if (!File.Exists(songJsonPath))
        {
            var defaultSongJson = @"{
  ""title"": ""Open The Heavens"",
  ""artist"": """",
  ""bpm"": 70.0742,
  ""timeSignatureNumerator"": 4,
  ""timeSignatureDenominator"": 4,
  ""beatInterval_ms"": 856.236,
  ""barDuration_ms"": 3424.943,
  ""gridAnchor_ms"": 792.381,
  ""originalKey"": ""C"",
  ""tracks"": [
    { ""file"": ""01_Click.mp3"", ""label"": ""Click"", ""output"": ""IEM"", ""defaultVolume"": 0.8, ""defaultMute"": false, ""color"": ""#FF6B6B"" },
    { ""file"": ""03_Drums.mp3"", ""label"": ""Drums"", ""output"": ""FOH"", ""defaultVolume"": 1.0, ""defaultMute"": false, ""color"": ""#F5A623"" },
    { ""file"": ""05_Bass.mp3"", ""label"": ""Bass"", ""output"": ""FOH"", ""defaultVolume"": 1.0, ""defaultMute"": false, ""color"": ""#7ED321"" },
    { ""file"": ""06_AcousticGuitar.mp3"", ""label"": ""Acoustic Guitar"", ""output"": ""FOH"", ""defaultVolume"": 1.0, ""defaultMute"": false, ""color"": ""#BD10E0"" },
    { ""file"": ""07_ElectricGuitar1.mp3"", ""label"": ""Electric Guitar"", ""output"": ""FOH"", ""defaultVolume"": 1.0, ""defaultMute"": false, ""color"": ""#4A90D9"" },
    { ""file"": ""11_Piano.mp3"", ""label"": ""Piano"", ""output"": ""FOH"", ""defaultVolume"": 1.0, ""defaultMute"": false, ""color"": ""#50E3C2"" },
    { ""file"": ""13_Keys1.mp3"", ""label"": ""Keys"", ""output"": ""FOH"", ""defaultVolume"": 1.0, ""defaultMute"": false, ""color"": ""#9B59B6"" },
    { ""file"": ""17_LeadVocal.mp3"", ""label"": ""Lead Vocal"", ""output"": ""FOH"", ""defaultVolume"": 1.0, ""defaultMute"": false, ""color"": ""#E74C3C"" }
  ],
  ""sections"": [
    { ""name"": ""INTRO"", ""loopIn_ms"": 792.381, ""loopOut_ms"": 17937.415, ""startBar"": 1, ""endBar"": 5, ""color"": ""#4A90D9"", ""lightingPresetIndex"": 0 },
    { ""name"": ""VERSE 1"", ""loopIn_ms"": 17937.415, ""loopOut_ms"": 48793.832, ""startBar"": 6, ""endBar"": 14, ""color"": ""#7ED321"", ""lightingPresetIndex"": 1 },
    { ""name"": ""VERSE 2"", ""loopIn_ms"": 48793.832, ""loopOut_ms"": 79650.249, ""startBar"": 15, ""endBar"": 23, ""color"": ""#7ED321"", ""lightingPresetIndex"": 2 },
    { ""name"": ""PRE-CHORUS"", ""loopIn_ms"": 79650.249, ""loopOut_ms"": 110506.667, ""startBar"": 24, ""endBar"": 32, ""color"": ""#F5A623"", ""lightingPresetIndex"": 3 },
    { ""name"": ""CHORUS 1"", ""loopIn_ms"": 110506.667, ""loopOut_ms"": 168791.655, ""startBar"": 33, ""endBar"": 49, ""color"": ""#E74C3C"", ""lightingPresetIndex"": 4 },
    { ""name"": ""BRIDGE"", ""loopIn_ms"": 168791.655, ""loopOut_ms"": 179078.095, ""startBar"": 50, ""endBar"": 52, ""color"": ""#9B59B6"", ""lightingPresetIndex"": 5 },
    { ""name"": ""VERSE 3"", ""loopIn_ms"": 179078.095, ""loopOut_ms"": 206506.667, ""startBar"": 53, ""endBar"": 60, ""color"": ""#7ED321"", ""lightingPresetIndex"": 6 },
    { ""name"": ""CHORUS 2 (FINAL)"", ""loopIn_ms"": 206506.667, ""loopOut_ms"": 278505.941, ""startBar"": 61, ""endBar"": 81, ""color"": ""#E74C3C"", ""lightingPresetIndex"": 7 },
    { ""name"": ""OUTRO / ENDING"", ""loopIn_ms"": 278505.941, ""loopOut_ms"": 302503.764, ""startBar"": 82, ""endBar"": 88, ""color"": ""#4A90D9"", ""lightingPresetIndex"": 8 }
  ],
  ""lightingPresets"": [
    { ""name"": ""Intro"", ""paletteIndex"": 0, ""intensity"": 0.3, ""excitement"": 0.2, ""snapshotName"": """", ""color"": ""#4A90D9"", ""midiNote"": 60, ""midiChannel"": 1, ""midiVelocity"": 100 },
    { ""name"": ""Verse 1"", ""paletteIndex"": 1, ""intensity"": 0.4, ""excitement"": 0.3, ""snapshotName"": """", ""color"": ""#7ED321"", ""midiNote"": 62, ""midiChannel"": 1, ""midiVelocity"": 100 },
    { ""name"": ""Verse 2"", ""paletteIndex"": 1, ""intensity"": 0.5, ""excitement"": 0.4, ""snapshotName"": """", ""color"": ""#7ED321"", ""midiNote"": 64, ""midiChannel"": 1, ""midiVelocity"": 110 },
    { ""name"": ""Pre-Chorus"", ""paletteIndex"": 2, ""intensity"": 0.6, ""excitement"": 0.5, ""snapshotName"": """", ""color"": ""#F5A623"", ""midiNote"": 65, ""midiChannel"": 1, ""midiVelocity"": 120 },
    { ""name"": ""Chorus"", ""paletteIndex"": 3, ""intensity"": 0.9, ""excitement"": 0.8, ""snapshotName"": """", ""color"": ""#E74C3C"", ""midiNote"": 67, ""midiChannel"": 1, ""midiVelocity"": 127 },
    { ""name"": ""Bridge"", ""paletteIndex"": 4, ""intensity"": 0.5, ""excitement"": 0.3, ""snapshotName"": """", ""color"": ""#9B59B6"", ""midiNote"": 69, ""midiChannel"": 1, ""midiVelocity"": 90 },
    { ""name"": ""Verse 3"", ""paletteIndex"": 1, ""intensity"": 0.4, ""excitement"": 0.3, ""snapshotName"": """", ""color"": ""#7ED321"", ""midiNote"": 62, ""midiChannel"": 1, ""midiVelocity"": 100 },
    { ""name"": ""Chorus Final"", ""paletteIndex"": 3, ""intensity"": 0.95, ""excitement"": 0.9, ""snapshotName"": """", ""color"": ""#E74C3C"", ""midiNote"": 67, ""midiChannel"": 1, ""midiVelocity"": 127 },
    { ""name"": ""Outro"", ""paletteIndex"": 0, ""intensity"": 0.2, ""excitement"": 0.1, ""snapshotName"": """", ""color"": ""#4A90D9"", ""midiNote"": 71, ""midiChannel"": 1, ""midiVelocity"": 80 }
  ]
}";
            File.WriteAllText(songJsonPath, defaultSongJson);
        }
    }

    private void PopulateDisplayManager(WorshipPlayer.Presenter.Services.DisplayManager displayManager)
    {
        // Use WPF-native PresentationSource + Win32 interop for monitor detection.
        // This avoids needing System.Windows.Forms (which causes type conflicts with WPF).
        try
        {
            // Get all monitors via WPF's SystemParameters and PresentationSource
            // Using the managed wrapper around EnumDisplayMonitors
            var monitors = GetAllMonitors();
            foreach (var monitor in monitors)
            {
                displayManager.RegisterDisplay(monitor);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to enumerate displays: {ex.Message}");
        }
    }

    /// <summary>
    /// Enumerate all connected monitors using Win32 API (no WinForms dependency).
    /// </summary>
    private static List<WorshipPlayer.Presenter.Services.DisplayInfo> GetAllMonitors()
    {
        var monitors = new List<WorshipPlayer.Presenter.Services.DisplayInfo>();

        // Use Win32 EnumDisplayMonitors via P/Invoke
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData) =>
        {
            var monitorInfo = new MONITORINFOEX();
            monitorInfo.cbSize = System.Runtime.InteropServices.Marshal.SizeOf(monitorInfo);

            if (GetMonitorInfo(hMonitor, ref monitorInfo))
            {
                monitors.Add(new WorshipPlayer.Presenter.Services.DisplayInfo
                {
                    Index = monitors.Count,
                    Name = new string(monitorInfo.szDevice).TrimEnd('\0'),
                    Left = monitorInfo.rcMonitor.Left,
                    Top = monitorInfo.rcMonitor.Top,
                    Width = monitorInfo.rcMonitor.Right - monitorInfo.rcMonitor.Left,
                    Height = monitorInfo.rcMonitor.Bottom - monitorInfo.rcMonitor.Top,
                    IsPrimary = (monitorInfo.dwFlags & 1) != 0 // MONITORINFOF_PRIMARY = 1
                });
            }
            return true;
        }, IntPtr.Zero);

        return monitors;
    }

    // Win32 P/Invoke for monitor enumeration (no WinForms needed)
    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 32)]
        public string szDevice;
    }
}
