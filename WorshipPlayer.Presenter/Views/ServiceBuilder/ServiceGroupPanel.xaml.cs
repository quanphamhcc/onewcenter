using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace WorshipPlayer.Presenter.Views.ServiceBuilder;

/// <summary>
/// Panel for managing collapsible service groups (e.g. Opening Worship, Message, Response).
/// Supports creating, renaming, deleting, and reordering groups.
/// </summary>
public partial class ServiceGroupPanel : UserControl
{
    public ObservableCollection<ServiceGroupItem> Groups { get; } = new();

    public event EventHandler<string>? GroupAdded;
    public event EventHandler<string>? GroupDeleted;
    public event EventHandler<(string OldName, string NewName)>? GroupRenamed;

    public ServiceGroupPanel()
    {
        InitializeComponent();
        GroupsItemsControl.ItemsSource = Groups;
        Groups.CollectionChanged += (_, _) => UpdateEmptyState();
    }

    private void AddGroup_Click(object sender, RoutedEventArgs e)
    {
        var name = PromptForName("New Group", "Enter group name:");
        if (!string.IsNullOrWhiteSpace(name))
        {
            Groups.Add(new ServiceGroupItem { Name = name });
            GroupAdded?.Invoke(this, name);
        }
    }

    private void ToggleGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn)
        {
            btn.Content = btn.Content?.ToString() == "▼" ? "►" : "▼";
        }
    }

    private void RenameGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string oldName)
        {
            var newName = PromptForName("Rename Group", $"Rename '{oldName}' to:");
            if (!string.IsNullOrWhiteSpace(newName))
            {
                var group = Groups.FirstOrDefault(g => g.Name == oldName);
                if (group != null)
                {
                    group.Name = newName;
                    GroupsItemsControl.Items.Refresh();
                    GroupRenamed?.Invoke(this, (oldName, newName));
                }
            }
        }
    }

    private void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string name)
        {
            var result = MessageBox.Show($"Delete group '{name}'?", "Confirm Delete",
                MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                var group = Groups.FirstOrDefault(g => g.Name == name);
                if (group != null)
                {
                    Groups.Remove(group);
                    GroupDeleted?.Invoke(this, name);
                }
            }
        }
    }

    private void UpdateEmptyState()
    {
        EmptyStateText.Visibility = Groups.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string? PromptForName(string title, string prompt)
    {
        // Simple input dialog - in production this would be a styled dialog
        var inputWindow = new Window
        {
            Title = title,
            Width = 350,
            Height = 150,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ResizeMode = ResizeMode.CanResize,
            Background = System.Windows.Media.Brushes.Black
        };

        var textBox = new TextBox { Margin = new Thickness(12), FontSize = 14 };
        var okButton = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 12, 12), HorizontalAlignment = HorizontalAlignment.Right };
        string? result = null;
        okButton.Click += (_, _) => { result = textBox.Text; inputWindow.Close(); };

        var panel = new DockPanel();
        panel.Children.Add(new TextBlock { Text = prompt, Margin = new Thickness(12, 12, 12, 4), Foreground = System.Windows.Media.Brushes.White });
        DockPanel.SetDock(panel.Children[0], Dock.Top);
        DockPanel.SetDock(okButton, Dock.Bottom);
        panel.Children.Add(okButton);
        panel.Children.Add(textBox);

        inputWindow.Content = panel;
        inputWindow.ShowDialog();
        return result;
    }
}

/// <summary>
/// Represents a named group within the service order sidebar.
/// </summary>
public class ServiceGroupItem
{
    public string Name { get; set; } = string.Empty;
    public bool IsExpanded { get; set; } = true;
    public List<string> ItemIds { get; set; } = new();
}
