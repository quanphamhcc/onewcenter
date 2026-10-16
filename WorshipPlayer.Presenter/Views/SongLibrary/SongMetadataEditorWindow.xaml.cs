using System.Windows;

namespace WorshipPlayer.Presenter.Views.SongLibrary;

/// <summary>
/// Thin modal host window for the <see cref="SongMetadataEditor"/> UserControl.
/// The editor cannot be shown as a dialog on its own (it is a UserControl), so this
/// window hosts it and bridges its Save/Cancel events to <see cref="Window.DialogResult"/>,
/// mirroring how <see cref="ChordProImportDialog"/> is consumed by OperatorWindow.
/// </summary>
public partial class SongMetadataEditorWindow : Window
{
    /// <summary>
    /// The metadata the user confirmed via Save, or null if the dialog was cancelled.
    /// </summary>
    public SongMetadata? Result { get; private set; }

    public SongMetadataEditorWindow(string title, string artist, string key, int? bpm, string? ccli, string tags)
    {
        InitializeComponent();

        Editor.LoadMetadata(title, artist, key, bpm, ccli, tags);
        Editor.SaveRequested += OnSaveRequested;
        Editor.CancelRequested += OnCancelRequested;
    }

    private void OnSaveRequested(object? sender, SongMetadata metadata)
    {
        Result = metadata;
        DialogResult = true;
        Close();
    }

    private void OnCancelRequested(object? sender, EventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
