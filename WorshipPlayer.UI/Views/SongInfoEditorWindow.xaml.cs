using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.UI.Views;

public partial class SongInfoEditorWindow : Window
{
    private readonly Song _song;

    public SongInfoEditorWindow(Song song)
    {
        InitializeComponent();
        _song = song;

        // Populate fields
        TitleBox.Text = song.Title;
        BpmBox.Text = song.Bpm.ToString("F2");
        ComposerBox.Text = song.Composer;
        LyricistBox.Text = song.Lyricist;
        ArrangerBox.Text = song.Arranger;
        ThemesBox.Text = song.Themes;

        // Set key combo
        string key = song.OriginalKey ?? "C";
        bool isMinor = key.EndsWith("m");
        string keyNote = isMinor ? key[..^1] : key;

        foreach (ComboBoxItem item in KeyCombo.Items)
        {
            if (item.Content.ToString() == keyNote)
            {
                KeyCombo.SelectedItem = item;
                break;
            }
        }
        KeyModeCombo.SelectedIndex = isMinor ? 1 : 0;

        // Set time signature combo
        string timeSig = $"{song.TimeSignatureNumerator}/{song.TimeSignatureDenominator}";
        foreach (ComboBoxItem item in TimeSigCombo.Items)
        {
            if (item.Content.ToString() == timeSig)
            {
                TimeSigCombo.SelectedItem = item;
                break;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        // Apply changes
        _song.Title = TitleBox.Text.Trim();
        _song.Composer = ComposerBox.Text.Trim();
        _song.Lyricist = LyricistBox.Text.Trim();
        _song.Arranger = ArrangerBox.Text.Trim();
        _song.Themes = ThemesBox.Text.Trim();

        // BPM
        if (double.TryParse(BpmBox.Text, out double bpm) && bpm > 0)
        {
            _song.Bpm = bpm;
            _song.RecalculateGrid();
        }

        // Key
        string keyNote = (KeyCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "C";
        bool isMinor = KeyModeCombo.SelectedIndex == 1;
        _song.OriginalKey = isMinor ? $"{keyNote}m" : keyNote;

        // Time Signature
        string timeSig = (TimeSigCombo.SelectedItem as ComboBoxItem)?.Content.ToString() ?? "4/4";
        var parts = timeSig.Split('/');
        if (parts.Length == 2 && int.TryParse(parts[0], out int num) && int.TryParse(parts[1], out int den))
        {
            _song.TimeSignatureNumerator = num;
            _song.TimeSignatureDenominator = den;
            _song.RecalculateGrid();
        }

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
