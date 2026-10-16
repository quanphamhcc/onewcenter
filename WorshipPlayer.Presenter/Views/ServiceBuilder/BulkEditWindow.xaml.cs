using System.Windows;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Views.ServiceBuilder;

/// <summary>
/// Thin modal host for <see cref="BulkEditPanel"/> (roadmap C3), mirroring how
/// <see cref="ItemSettingsWindow"/> hosts <see cref="ItemSettingsPanel"/>. Exposes the chosen
/// <see cref="BulkEditOptions"/> as <see cref="Result"/> once the user clicks "Apply".
/// The panel's <c>DeleteRequested</c> event is intentionally not surfaced here — C3 is scoped to
/// batch-applying background/transition/auto-advance, not bulk delete.
/// </summary>
public partial class BulkEditWindow : Window
{
    /// <summary>The opted-in options produced by the panel's Apply, available after a true DialogResult.</summary>
    public BulkEditOptions? Result { get; private set; }

    public BulkEditWindow(int selectedCount)
    {
        InitializeComponent();

        BulkEditPanelControl.SetSelectedCount(selectedCount);
        BulkEditPanelControl.ApplyRequested += OnApplyRequested;
    }

    private void OnApplyRequested(object? sender, BulkEditOptions options)
    {
        Result = options;
        DialogResult = true;
        Close();
    }
}
