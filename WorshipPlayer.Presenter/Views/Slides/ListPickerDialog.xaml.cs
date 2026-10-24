using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace WorshipPlayer.Presenter.Views.Slides;

/// <summary>
/// Small reusable owned modal list picker. Its own <see cref="Window"/> that MERGES
/// OwcDesignSystem.xaml (keyed dark styles), used by <see cref="SlideServiceItemEditorWindow"/>
/// for the presentation and template pickers. Lists <see cref="PickerRow"/> entries by their
/// <see cref="PickerRow.Label"/> and returns the chosen <see cref="PickerRow.Id"/> via
/// <see cref="SelectedId"/> when it closes with <c>DialogResult == true</c>.
/// </summary>
public partial class ListPickerDialog : Window
{
    /// <summary>A single selectable row: an id plus its display label.</summary>
    public sealed class PickerRow
    {
        public PickerRow(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }
        public string Label { get; }
    }

    /// <summary>The chosen row's id, non-null only after the dialog closed with true.</summary>
    public string? SelectedId { get; private set; }

    public ListPickerDialog(string header, IReadOnlyList<PickerRow> rows, string? preselectedId)
    {
        InitializeComponent();

        // The audience/output window (OutputWindow) runs Topmost=true while live and would
        // otherwise sit ABOVE this modal and swallow the operator's clicks/taps (symptom:
        // "Cancel is very hard to press"). Make this dialog Topmost + active so it always sits
        // above the output window and receives input on the first try, matching the
        // SlideServiceItemEditorWindow fix. Activate on load to take foreground.
        Topmost = true;
        Loaded += (_, _) => { Activate(); };

        HeaderText.Text = header;
        ItemsListBox.ItemsSource = rows;

        if (preselectedId != null)
        {
            foreach (var row in rows)
            {
                if (row.Id == preselectedId)
                {
                    ItemsListBox.SelectedItem = row;
                    break;
                }
            }
        }
    }

    private void Select_Click(object sender, RoutedEventArgs e) => Commit();

    private void ItemsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e) => Commit();

    private void Commit()
    {
        if (ItemsListBox.SelectedItem is PickerRow row)
        {
            SelectedId = row.Id;
            DialogResult = true;
            Close();
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
