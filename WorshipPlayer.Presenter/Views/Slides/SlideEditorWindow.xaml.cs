using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Slides;

/// <summary>
/// Modal editor for a single <see cref="SlidePresentation"/> (Stage 2). Its own Window that MERGES
/// OwcDesignSystem.xaml (so it may use the keyed dark styles, unlike OperatorWindow), mirroring
/// <see cref="SongLibrary.SongMetadataEditorWindow"/> and <see cref="SlideNameDialog"/>.
///
/// The passed presentation is a fresh <see cref="ISlidePresentationService.GetByIdAsync"/> object
/// and is treated as the editable copy: structural edits go through <see cref="SlideOperations"/>
/// (which keeps Order dense/0-based) and nothing is persisted until "Save and close" calls
/// <see cref="ISlidePresentationService.UpdateAsync"/>. Close / window-X discard all changes.
/// </summary>
public partial class SlideEditorWindow : Window
{
    private readonly SlidePresentation _presentation;
    private readonly ISlidePresentationService _service;
    private readonly ObservableCollection<SlideRow> _rows = new();
    private bool _saving;
    private bool _suppressTextSync;

    /// <summary>The persisted presentation when the window returned true; null if discarded.</summary>
    public SlidePresentation? Result { get; private set; }

    public SlideEditorWindow(SlidePresentation presentation, ISlidePresentationService service)
    {
        InitializeComponent();

        _presentation = presentation;
        _service = service;

        // Keep Order dense/0-based before binding (handles legacy/sparse rows).
        SlideOperations.Normalize(_presentation.Slides);

        NameTextBox.Text = _presentation.Name;

        SlidesListBox.ItemsSource = _rows;
        RebuildRows();

        if (_rows.Count > 0)
            SlidesListBox.SelectedIndex = 0;

        LoadSelectedSlideText();
        UpdatePlaceholder();
    }

    /// <summary>Rebuilds the row view-models from the (already normalized) Slides list.</summary>
    private void RebuildRows()
    {
        _rows.Clear();
        for (int i = 0; i < _presentation.Slides.Count; i++)
            _rows.Add(new SlideRow(_presentation.Slides[i], i));
    }

    private PresentationSlide? SelectedSlide
    {
        get
        {
            int index = SlidesListBox.SelectedIndex;
            return index >= 0 && index < _presentation.Slides.Count
                ? _presentation.Slides[index]
                : null;
        }
    }

    private void AddSlide_Click(object sender, RoutedEventArgs e)
    {
        SlideOperations.AddSlide(_presentation.Slides);
        RebuildRows();
        SlidesListBox.SelectedIndex = _rows.Count - 1;
        SlidesListBox.ScrollIntoView(SlidesListBox.SelectedItem);
    }

    private void DeleteSlide_Click(object sender, RoutedEventArgs e)
    {
        int index = SlidesListBox.SelectedIndex;
        if (index < 0) return;

        SlideOperations.DeleteSlide(_presentation.Slides, index);
        RebuildRows();

        // Reselect a valid neighbour (the item now at the old index, else the previous one).
        if (_rows.Count == 0)
            SlidesListBox.SelectedIndex = -1;
        else
            SlidesListBox.SelectedIndex = System.Math.Min(index, _rows.Count - 1);
    }

    private void MoveUp_Click(object sender, RoutedEventArgs e)
    {
        int index = SlidesListBox.SelectedIndex;
        if (index <= 0) return;

        SlideOperations.MoveUp(_presentation.Slides, index);
        RebuildRows();
        SlidesListBox.SelectedIndex = index - 1;
        SlidesListBox.ScrollIntoView(SlidesListBox.SelectedItem);
    }

    private void MoveDown_Click(object sender, RoutedEventArgs e)
    {
        int index = SlidesListBox.SelectedIndex;
        if (index < 0 || index >= _rows.Count - 1) return;

        SlideOperations.MoveDown(_presentation.Slides, index);
        RebuildRows();
        SlidesListBox.SelectedIndex = index + 1;
        SlidesListBox.ScrollIntoView(SlidesListBox.SelectedItem);
    }

    private void SlidesListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        LoadSelectedSlideText();
        UpdatePlaceholder();
    }

    private void LoadSelectedSlideText()
    {
        _suppressTextSync = true;
        try
        {
            var slide = SelectedSlide;
            SlideTextBox.Text = slide?.Text ?? "";
            SlideTextBox.IsEnabled = slide != null;
        }
        finally
        {
            _suppressTextSync = false;
        }
    }

    private void SlideTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_suppressTextSync)
        {
            var slide = SelectedSlide;
            if (slide != null)
            {
                slide.Text = SlideTextBox.Text;
                // Refresh the preview shown in the list for the current row.
                int index = SlidesListBox.SelectedIndex;
                if (index >= 0 && index < _rows.Count)
                    _rows[index].Refresh();
            }
        }

        UpdatePlaceholder();
    }

    private void UpdatePlaceholder()
    {
        bool show = SlideTextBox.IsEnabled && string.IsNullOrEmpty(SlideTextBox.Text);
        PlaceholderText.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void SaveAndClose_Click(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        _saving = true;

        try
        {
            // A blank name keeps the prior non-empty name; the service also normalizes blank
            // input to "New Presentation" as a final safety net.
            var entered = NameTextBox.Text?.Trim() ?? "";
            if (!string.IsNullOrEmpty(entered))
                _presentation.Name = entered;

            await _service.UpdateAsync(_presentation);

            Result = _presentation;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _saving = false;
            MessageBox.Show(this,
                $"Failed to save the presentation: {ex.Message}",
                "Save Slides",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Discard: persist nothing.
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// List row view-model: wraps a <see cref="PresentationSlide"/> and exposes the display
    /// "Slide N" title and a trimmed single-line preview for the ListBox item template.
    /// </summary>
    private sealed class SlideRow : INotifyPropertyChanged
    {
        private readonly PresentationSlide _slide;

        public SlideRow(PresentationSlide slide, int displayIndex)
        {
            _slide = slide;
            Title = $"Slide {displayIndex + 1}";
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Title { get; }

        public string Preview
        {
            get
            {
                var text = _slide.Text?.Trim() ?? "";
                if (text.Length == 0)
                    return "(empty)";

                var firstLine = text.Replace("\r\n", "\n").Split('\n')[0].Trim();
                return firstLine.Length > 60 ? firstLine.Substring(0, 60) + "…" : firstLine;
            }
        }

        /// <summary>Notifies the bound list item to re-read <see cref="Preview"/> as text changes.</summary>
        public void Refresh() =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
    }
}
