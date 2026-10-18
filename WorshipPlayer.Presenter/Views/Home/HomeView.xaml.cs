using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;

namespace WorshipPlayer.Presenter.Views.Home;

/// <summary>
/// WorshipTools-style Home landing screen: a Quick start card of icon buttons and an
/// Upcoming services table. This UserControl MERGES OwcDesignSystem.xaml in its own
/// resource scope (like ServiceInfoDialog), so it may safely use the keyed dark styles —
/// unlike OperatorWindow, which hardcodes brushes. The view is a passive renderer:
/// the host calls <see cref="SetServices"/> and listens to the typed events below.
/// </summary>
public partial class HomeView : UserControl
{
    public event EventHandler? TourRequested;
    public event EventHandler? ServicesRequested;
    public event EventHandler? SongsRequested;
    public event EventHandler? SlidesRequested;
    public event EventHandler? MediaRequested;
    public event EventHandler? ScriptureRequested;
    public event EventHandler? AddServiceRequested;
    public event EventHandler<string>? OpenServiceRequested;
    public event EventHandler<string>? EditServiceRequested;
    public event EventHandler<string>? RenameServiceRequested;
    public event EventHandler<string>? DeleteServiceRequested;

    public HomeView()
    {
        InitializeComponent();
    }

    /// <summary>(Re)bind the Upcoming services table.</summary>
    public void SetServices(IEnumerable<ServiceListRow> rows)
    {
        ServicesListView.ItemsSource = rows;
    }

    private void Tour_Click(object sender, RoutedEventArgs e) => TourRequested?.Invoke(this, EventArgs.Empty);
    private void Services_Click(object sender, RoutedEventArgs e) => ServicesRequested?.Invoke(this, EventArgs.Empty);
    private void Songs_Click(object sender, RoutedEventArgs e) => SongsRequested?.Invoke(this, EventArgs.Empty);
    private void Slides_Click(object sender, RoutedEventArgs e) => SlidesRequested?.Invoke(this, EventArgs.Empty);
    private void Media_Click(object sender, RoutedEventArgs e) => MediaRequested?.Invoke(this, EventArgs.Empty);
    private void Scripture_Click(object sender, RoutedEventArgs e) => ScriptureRequested?.Invoke(this, EventArgs.Empty);
    private void AddService_Click(object sender, RoutedEventArgs e) => AddServiceRequested?.Invoke(this, EventArgs.Empty);

    // The overflow glyph button opens its own ContextMenu; the actual actions are the menu items.
    private void Overflow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.ContextMenu != null)
        {
            btn.ContextMenu.PlacementTarget = btn;
            btn.ContextMenu.IsOpen = true;
        }
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (ResolvePath(sender) is { } path)
            OpenServiceRequested?.Invoke(this, path);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (ResolvePath(sender) is { } path)
            EditServiceRequested?.Invoke(this, path);
    }

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        if (ResolvePath(sender) is { } path)
            RenameServiceRequested?.Invoke(this, path);
    }

    private void Delete_Click(object sender, RoutedEventArgs e)
    {
        if (ResolvePath(sender) is { } path)
            DeleteServiceRequested?.Invoke(this, path);
    }

    private static string? ResolvePath(object sender)
        => (sender as FrameworkElement)?.Tag as string;
}
