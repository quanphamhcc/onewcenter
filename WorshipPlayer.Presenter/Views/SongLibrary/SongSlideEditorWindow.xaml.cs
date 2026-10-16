using System.Collections.Generic;
using System.Windows;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Presenter.Views.SongLibrary;

/// <summary>
/// Thin modal host window for the <see cref="SongSlideEditor"/> UserControl.
/// The editor cannot be shown as a dialog on its own (it is a UserControl), so this
/// window hosts it and bridges its Save/Cancel events to <see cref="Window.DialogResult"/>,
/// mirroring <see cref="SongMetadataEditorWindow"/>.
/// </summary>
public partial class SongSlideEditorWindow : Window
{
    /// <summary>
    /// The edited section list the user confirmed via Save, or null if cancelled.
    /// </summary>
    public List<Section>? Result { get; private set; }

    public SongSlideEditorWindow(
        List<Section> sections,
        IEnumerable<string> imagePaths,
        IEnumerable<(string Id, string Name)> templates)
    {
        InitializeComponent();

        Editor.LoadSong(sections, imagePaths, templates);
        Editor.SaveRequested += OnSaveRequested;
        Editor.CancelRequested += OnCancelRequested;
    }

    private void OnSaveRequested(object? sender, SlideEditResult result)
    {
        Result = result.Sections;
        DialogResult = true;
        Close();
    }

    private void OnCancelRequested(object? sender, EventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
