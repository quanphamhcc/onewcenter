using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.UI.Views;

/// <summary>
/// MIDI Cue Editor for WorshipTools Presenter integration.
/// 
/// Allows users to:
/// - Add/edit/delete MIDI cues at specific timestamps
/// - Record mode: tap SPACE during playback to place "Next Slide" cues
/// - Configure action type, note, velocity, channel per cue
/// - Test MIDI connection
/// </summary>
public partial class MidiCueEditorWindow : Window
{
    private readonly Song _song;
    private readonly MidiCueService? _midiCueService;
    private readonly Func<double>? _getPositionMs;
    private readonly ObservableCollection<MidiCueViewModel> _cues = new();
    private bool _recordMode = false;

    /// <summary>
    /// Static property for XAML ComboBox binding.
    /// </summary>
    public static string[] ActionOptions => new[] { "NextSlide", "PrevSlide", "NextItem", "TriggerSlide" };

    public MidiCueEditorWindow(Song song, MidiCueService? midiCueService = null,
                                Func<double>? getPositionMs = null)
    {
        InitializeComponent();

        _song = song;
        _midiCueService = midiCueService;
        _getPositionMs = getPositionMs;

        TitleText.Text = $"MIDI Cue Editor — {song.Title}";
        SongInfoText.Text = $"BPM: {song.Bpm:F1} | Sections: {song.Sections.Count} | " +
                           $"Existing cues: {song.MidiCues.Count}";

        // Load existing cues
        LoadCues();

        CueGrid.ItemsSource = _cues;

        // Handle keyboard for record mode
        KeyDown += MidiCueEditorWindow_KeyDown;
    }

    private void LoadCues()
    {
        _cues.Clear();
        var sorted = _song.MidiCues.OrderBy(c => c.TimestampMs).ToList();
        for (int i = 0; i < sorted.Count; i++)
        {
            var cue = sorted[i];
            _cues.Add(new MidiCueViewModel
            {
                Index = i + 1,
                TimestampMs = cue.TimestampMs,
                Action = cue.Action,
                Channel = cue.Channel,
                Note = cue.Note,
                Velocity = cue.Velocity,
                SlideNumber = cue.SlideNumber,
                Label = cue.Label,
                Enabled = cue.Enabled
            });
        }
    }

    private void ReindexCues()
    {
        var sorted = _cues.OrderBy(c => c.TimestampMs).ToList();
        _cues.Clear();
        for (int i = 0; i < sorted.Count; i++)
        {
            sorted[i].Index = i + 1;
            _cues.Add(sorted[i]);
        }
    }

    // ─── ADD / DELETE ──────────────────────────────────────────────────────

    private void AddCue_Click(object sender, RoutedEventArgs e)
    {
        double timestampMs = ParseTimestamp(TimestampBox.Text);
        if (timestampMs < 0)
        {
            MessageBox.Show("Enter a valid timestamp (M:SS:MMM or milliseconds).",
                "Invalid Timestamp", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var action = (MidiCueAction)ActionComboBox.SelectedIndex;

        _cues.Add(new MidiCueViewModel
        {
            Index = _cues.Count + 1,
            TimestampMs = timestampMs,
            Action = action,
            Channel = 0, // 0 = use default
            Note = 0,    // 0 = use default for action
            Velocity = 127,
            SlideNumber = 0,
            Label = $"Cue {_cues.Count + 1}",
            Enabled = true
        });

        ReindexCues();
    }

    private void DeleteCue_Click(object sender, RoutedEventArgs e)
    {
        if (CueGrid.SelectedItem is MidiCueViewModel selected)
        {
            _cues.Remove(selected);
            ReindexCues();
        }
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (_cues.Count == 0) return;

        var result = MessageBox.Show(
            $"Delete all {_cues.Count} MIDI cues?",
            "Clear All Cues", MessageBoxButton.YesNo, MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            _cues.Clear();
        }
    }

    // ─── RECORD MODE ──────────────────────────────────────────────────────

    private void Record_Click(object sender, RoutedEventArgs e)
    {
        _recordMode = !_recordMode;

        if (_recordMode)
        {
            RecordBtn.Background = System.Windows.Media.Brushes.Red;
            RecordBtn.Content = "RECORDING (SPACE = add cue)";
            RecordStatusText.Text = "Press SPACE to place 'Next Slide' cue at current playback position";
        }
        else
        {
            RecordBtn.Background = System.Windows.Media.Brushes.DarkRed;
            RecordBtn.Content = "RECORD MODE";
            RecordStatusText.Text = "";
        }
    }

    private void MidiCueEditorWindow_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Space && _recordMode && _getPositionMs != null)
        {
            double posMs = _getPositionMs();
            if (posMs > 0)
            {
                _cues.Add(new MidiCueViewModel
                {
                    Index = _cues.Count + 1,
                    TimestampMs = posMs,
                    Action = MidiCueAction.NextSlide,
                    Channel = 0,
                    Note = 0,
                    Velocity = 127,
                    SlideNumber = 0,
                    Label = $"[Rec] {MsToTimecode(posMs)}",
                    Enabled = true
                });

                ReindexCues();
                RecordStatusText.Text = $"Cue added at {MsToTimecode(posMs)} ({_cues.Count} total)";

                // Scroll to the new cue
                if (_cues.Count > 0)
                    CueGrid.ScrollIntoView(_cues.Last());
            }
            e.Handled = true;
        }
    }

    // ─── TEST & SAVE ──────────────────────────────────────────────────────

    private void TestSend_Click(object sender, RoutedEventArgs e)
    {
        if (_midiCueService == null)
        {
            MessageBox.Show("MIDI Cue Service not available.",
                "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        if (!_midiCueService.IsConnected)
        {
            MessageBox.Show(
                "MIDI output not connected.\n\n" +
                "Configure MIDI Presenter settings in the main window:\n" +
                "1. Install loopMIDI (virtual MIDI port)\n" +
                "2. Enable MIDI Presenter in settings\n" +
                "3. Select the MIDI output device",
                "Not Connected", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool sent = _midiCueService.SendTestMessage();
        if (sent)
        {
            MessageBox.Show(
                $"Test MIDI Note On sent!\n\n" +
                $"Device: {_midiCueService.ConnectedDeviceName}\n" +
                $"Check WorshipTools Presenter for response.",
                "Test Sent", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Convert view models back to MidiCue objects
        _song.MidiCues.Clear();
        foreach (var vm in _cues.OrderBy(c => c.TimestampMs))
        {
            _song.MidiCues.Add(new MidiCue
            {
                TimestampMs = vm.TimestampMs,
                Action = vm.Action,
                Channel = vm.Channel,
                Note = vm.Note,
                Velocity = vm.Velocity,
                SlideNumber = vm.SlideNumber,
                Label = vm.Label,
                Enabled = vm.Enabled
            });
        }

        // Reload cues into the service for immediate use
        _midiCueService?.LoadSong(_song);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    // ─── HELPERS ──────────────────────────────────────────────────────────

    private double ParseTimestamp(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return -1;
        input = input.Trim();

        // Try milliseconds
        if (double.TryParse(input, out double ms) && !input.Contains(':'))
            return ms;

        // Try M:SS:MMM or M:SS.MMM
        var parts = input.Replace('.', ':').Split(':');
        if (parts.Length == 3)
        {
            if (int.TryParse(parts[0], out int min) &&
                int.TryParse(parts[1], out int sec) &&
                int.TryParse(parts[2], out int millis))
            {
                return min * 60000 + sec * 1000 + millis;
            }
        }
        else if (parts.Length == 2)
        {
            if (int.TryParse(parts[0], out int min) &&
                int.TryParse(parts[1], out int sec))
            {
                return min * 60000 + sec * 1000;
            }
        }

        return -1;
    }

    private static string MsToTimecode(double ms)
    {
        int totalMs = (int)Math.Round(ms);
        int minutes = totalMs / 60000;
        int seconds = (totalMs % 60000) / 1000;
        int millis = totalMs % 1000;
        return $"{minutes}:{seconds:D2}.{millis:D3}";
    }
}

/// <summary>
/// ViewModel for MIDI cue editing grid.
/// </summary>
public class MidiCueViewModel : INotifyPropertyChanged
{
    private int _index;
    private double _timestampMs;
    private MidiCueAction _action = MidiCueAction.NextSlide;
    private int _channel;
    private int _note;
    private int _velocity = 127;
    private int _slideNumber;
    private string _label = "";
    private bool _enabled = true;

    public int Index
    {
        get => _index;
        set { _index = value; OnPropertyChanged(nameof(Index)); }
    }

    public double TimestampMs
    {
        get => _timestampMs;
        set { _timestampMs = value; OnPropertyChanged(nameof(TimestampMs)); OnPropertyChanged(nameof(TimestampDisplay)); }
    }

    public string TimestampDisplay => MsToTimecode(_timestampMs);

    public MidiCueAction Action
    {
        get => _action;
        set { _action = value; OnPropertyChanged(nameof(Action)); OnPropertyChanged(nameof(ActionDisplay)); }
    }

    public string ActionDisplay
    {
        get => _action.ToString();
        set
        {
            if (Enum.TryParse<MidiCueAction>(value, out var parsed))
            {
                _action = parsed;
                OnPropertyChanged(nameof(Action));
                OnPropertyChanged(nameof(ActionDisplay));
            }
        }
    }

    public int Channel
    {
        get => _channel;
        set { _channel = value; OnPropertyChanged(nameof(Channel)); }
    }

    public int Note
    {
        get => _note;
        set { _note = value; OnPropertyChanged(nameof(Note)); }
    }

    public int Velocity
    {
        get => _velocity;
        set { _velocity = value; OnPropertyChanged(nameof(Velocity)); }
    }

    public int SlideNumber
    {
        get => _slideNumber;
        set { _slideNumber = value; OnPropertyChanged(nameof(SlideNumber)); }
    }

    public string Label
    {
        get => _label;
        set { _label = value; OnPropertyChanged(nameof(Label)); }
    }

    public bool Enabled
    {
        get => _enabled;
        set { _enabled = value; OnPropertyChanged(nameof(Enabled)); }
    }

    private static string MsToTimecode(double ms)
    {
        int totalMs = (int)Math.Round(ms);
        int minutes = totalMs / 60000;
        int seconds = (totalMs % 60000) / 1000;
        int millis = totalMs % 1000;
        return $"{minutes}:{seconds:D2}.{millis:D3}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged(string name) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
