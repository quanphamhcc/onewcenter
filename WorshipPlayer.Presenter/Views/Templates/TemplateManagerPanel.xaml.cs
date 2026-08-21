using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Templates;

/// <summary>
/// Template management panel - list, create, duplicate, delete, rename templates.
/// </summary>
public partial class TemplateManagerPanel : UserControl
{
    private ITemplateService? _templateService;

    public ITemplateService? TemplateService
    {
        get => _templateService;
        set
        {
            _templateService = value;
            _ = RefreshTemplatesAsync();
        }
    }

    public ObservableCollection<TemplateListItem> Templates { get; } = new();

    public event EventHandler<TemplateListItem>? TemplateSelected;
    public event EventHandler<TemplateListItem>? EditTemplateRequested;

    public TemplateManagerPanel()
    {
        InitializeComponent();
        TemplateListBox.ItemsSource = Templates;
        Templates.CollectionChanged += (_, _) => UpdateEmptyState();
    }

    public async Task RefreshTemplatesAsync()
    {
        if (_templateService == null) return;

        var templates = await _templateService.GetAllAsync();
        Templates.Clear();
        foreach (var t in templates)
        {
            Templates.Add(new TemplateListItem
            {
                Id = t.Id,
                Name = t.Name,
                Category = t.TemplateCategory.ToString(),
                IsBuiltIn = t.IsBuiltIn,
                PreviewColor = t.FontColor,
                LockVisibility = t.IsBuiltIn ? Visibility.Visible : Visibility.Collapsed
            });
        }
    }

    private void NewButton_Click(object sender, RoutedEventArgs e)
    {
        // Create a new template via the editor window
        var editor = new TemplateEditorWindow
        {
            TemplateService = _templateService,
            Owner = Window.GetWindow(this)
        };
        editor.ShowDialog();
        if (editor.WasSaved)
            _ = RefreshTemplatesAsync();
    }

    private async void DuplicateButton_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is not TemplateListItem selected) return;
        if (_templateService == null) return;

        await _templateService.DuplicateAsync(selected.Id, $"{selected.Name} (Copy)");
        await RefreshTemplatesAsync();
    }

    private void RenameButton_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is not TemplateListItem selected) return;
        if (selected.IsBuiltIn) return;

        // Rename would typically show an input dialog
        // For now, the editor window can handle renames
        EditTemplateRequested?.Invoke(this, selected);
    }

    private async void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is not TemplateListItem selected) return;
        if (selected.IsBuiltIn) return;
        if (_templateService == null) return;

        var result = MessageBox.Show(
            $"Delete template '{selected.Name}'?",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await _templateService.DeleteAsync(selected.Id);
            await RefreshTemplatesAsync();
        }
    }

    private void TemplateList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TemplateListBox.SelectedItem is TemplateListItem item)
        {
            TemplateSelected?.Invoke(this, item);

            // Update delete button state
            DeleteButton.IsEnabled = !item.IsBuiltIn;
            RenameButton.IsEnabled = !item.IsBuiltIn;
        }
    }

    private void UpdateEmptyState()
    {
        bool hasCustom = Templates.Any(t => !t.IsBuiltIn);
        EmptyStateText.Visibility = !hasCustom && Templates.Count <= 3
            ? Visibility.Visible : Visibility.Collapsed;
    }
}

/// <summary>
/// Display model for template list items.
/// </summary>
public class TemplateListItem
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public bool IsBuiltIn { get; set; }
    public string PreviewColor { get; set; } = "#FFFFFF";
    public Visibility LockVisibility { get; set; } = Visibility.Collapsed;
}
