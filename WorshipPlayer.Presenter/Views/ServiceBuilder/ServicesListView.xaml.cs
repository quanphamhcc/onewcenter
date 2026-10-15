using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;

namespace WorshipPlayer.Presenter.Views.ServiceBuilder;

/// <summary>
/// Services-list table shown on the Services top-nav tab: Date / Type / Title /
/// Last modified / Last modified by, a search box, a Filters placeholder combo, a
/// + New Service button, and per-row overflow menus (Open / Rename / Delete). This
/// UserControl MERGES OwcDesignSystem.xaml, so it may use the keyed dark styles.
/// The view is a passive renderer: the host calls <see cref="SetServices"/> and listens
/// to the typed events; search filtering is done purely via Core's ServiceListingBuilder.
/// </summary>
public partial class ServicesListView : UserControl
{
    private List<ServiceListRow> _allRows = new();

    public event EventHandler? NewServiceRequested;
    public event EventHandler<string>? OpenServiceRequested;
    public event EventHandler<string>? RenameServiceRequested;
    public event EventHandler<string>? DeleteServiceRequested;

    public ServicesListView()
    {
        InitializeComponent();
    }

    /// <summary>Store the full list and (re)bind, honoring the current search text.</summary>
    public void SetServices(IEnumerable<ServiceListRow> rows)
    {
        _allRows = rows?.ToList() ?? new List<ServiceListRow>();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        var query = SearchTextBox?.Text ?? string.Empty;
        ServicesList.ItemsSource = ServiceListingBuilder.Filter(_allRows, query).ToList();
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

    private void NewService_Click(object sender, RoutedEventArgs e)
        => NewServiceRequested?.Invoke(this, EventArgs.Empty);

    private void ServicesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ServicesList.SelectedItem is ServiceListRow row && !string.IsNullOrEmpty(row.FilePath))
        {
            // Clear the selection so the same row can be re-opened later.
            ServicesList.SelectedItem = null;
            OpenServiceRequested?.Invoke(this, row.FilePath);
        }
    }

    // The overflow glyph button opens its own ContextMenu; the menu items are the actions.
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
