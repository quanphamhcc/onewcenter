using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using WorshipPlayer.Core.Models;
using WorshipPlayer.Core.Services;
using WorshipPlayer.Core.Services.Interfaces;

namespace WorshipPlayer.Presenter.Views.Slides;

/// <summary>
/// Full-featured modal editor for a single Slides-type <see cref="ServiceItem"/> that is already
/// part of the loaded service order. Its own <see cref="Window"/> that MERGES OwcDesignSystem.xaml
/// (keyed dark styles, like <see cref="SlideEditorWindow"/> — NOT OperatorWindow's inline brushes).
///
/// The editor operates on a deep-cloned working copy of the item; <c>Close</c>/X discards, and all
/// persistence/rename is deferred to Save-and-close. It keeps the item a LIVE LINK — Select sets
/// <see cref="ServiceItem.SlidePresentationId"/> only and never snapshots the presentation name or
/// content. Remove raises <see cref="RemoveRequested"/> and nothing else; the host owns closing.
/// Routing into OperatorWindow is FEAT-003.
/// </summary>
public partial class SlideServiceItemEditorWindow : Window
{
    // ─── Window-local clone options (Revision-2 Finding 5) ─────────────────────────────────────
    // Intentionally NOT the manager's _jsonOptions: the clone never hits disk, so it must preserve
    // nulls (NO WhenWritingNull) and needs no WriteIndented. Only the enum converter is required.
    private static readonly JsonSerializerOptions _cloneJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ServiceItem _live;
    private readonly ServiceItem _working;
    private readonly IReadOnlyList<ServiceItem> _allItems;
    private readonly ISlidePresentationService _slidePresentationService;
    private readonly IMediaLibraryService _mediaLibraryService;
    private readonly ITemplateService _templateService;

    private readonly ObservableCollection<AudioRow> _audio = new();
    private readonly ObservableCollection<SourceRow> _sources = new();
    private readonly ObservableCollection<TargetRow> _targets = new();

    private bool _suppressDirty = true;
    private bool _isDirty;
    private bool _saving;

    // Deferred rename state for a live-linked item (applied only on save).
    private string? _pendingPresentationName;

    // Selected template id in the working set (mirrors _working.StageTemplateId once edited).
    private string? _selectedTemplateId;

    /// <summary>Raised ONLY by the Remove button. The host owns closing (sets DialogResult=false).</summary>
    public event EventHandler? RemoveRequested;

    /// <summary>
    /// Raised after the inner slide CONTENT editor (<see cref="SlideEditorWindow"/>) persists edits
    /// to the linked presentation. Lets the host re-prime its cache / refresh the center grid so
    /// newly-added slides show immediately, even if the user later Closes (discards) this editor.
    /// This is NOT a service-item edit — it must never arm the dirty flag.
    /// </summary>
    public event EventHandler? LinkedPresentationSlidesEdited;

    /// <summary>The live item after a successful save; null if discarded or removed.</summary>
    public ServiceItem? Result { get; private set; }

    public SlideServiceItemEditorWindow(
        ServiceItem item,
        IReadOnlyList<ServiceItem> allItems,
        ISlidePresentationService slidePresentationService,
        IMediaLibraryService mediaLibraryService,
        ITemplateService templateService)
    {
        InitializeComponent();

        _live = item ?? throw new ArgumentNullException(nameof(item));
        _allItems = allItems ?? throw new ArgumentNullException(nameof(allItems));
        _slidePresentationService = slidePresentationService ?? throw new ArgumentNullException(nameof(slidePresentationService));
        _mediaLibraryService = mediaLibraryService ?? throw new ArgumentNullException(nameof(mediaLibraryService));
        _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));

        // Deep-clone the live item so Close/X can discard cleanly (JSON round-trip).
        var json = JsonSerializer.Serialize(_live, _cloneJsonOptions);
        _working = JsonSerializer.Deserialize<ServiceItem>(json, _cloneJsonOptions)!;

        _suppressDirty = true;

        AudioItemsControl.ItemsSource = _audio;
        SourceComboBox.ItemsSource = _sources;
        TargetComboBox.ItemsSource = _targets;

        Loaded += OnLoaded;
    }

    // ─── Population (all awaited work under _suppressDirty) ─────────────────────────────────────

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        // Guard already set true in the ctor; keep it true across the whole awaited sequence.
        _suppressDirty = true;
        try
        {
            await PopulateLinkedPresentationAsync();
            await PopulateSourcesAsync();
            await PopulateTargetsAsync();
            PopulateAutoAdvanceDropdowns();
            PopulateAudio();
            PopulateTransition();
            await PopulateTemplateAsync();
        }
        finally
        {
            _suppressDirty = false;
            Debug.Assert(_isDirty == false, "Initial population must not arm the dirty flag.");
            UpdateSaveEnabled();
        }
    }

    private async System.Threading.Tasks.Task PopulateLinkedPresentationAsync()
    {
        _pendingPresentationName = null;

        if (string.IsNullOrEmpty(_working.SlidePresentationId))
        {
            // Non-linked (legacy snapshot) item: header/display driven by Title; rename allowed.
            NameTextBlock.Text = _working.Title;
            LinkedPresentationText.Text = "(no presentation linked)";
            UpdateEditSlidesButtonState(null);
            return;
        }

        SlidePresentation? linked = null;
        try
        {
            linked = await _slidePresentationService.GetByIdAsync(_working.SlidePresentationId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SlideServiceItemEditor] linked presentation load failed: {ex.Message}");
        }

        if (linked == null)
        {
            LinkedPresentationText.Text = "Linked presentation unavailable";
            NameTextBlock.Text = ServiceItemDisplayName.ResolveDisplayName(_working, null);
            RenameButton.IsEnabled = false; // rename disabled for the missing-linked case
        }
        else
        {
            LinkedPresentationText.Text = linked.Name;
            NameTextBlock.Text = ServiceItemDisplayName.ResolveDisplayName(_working, linked);
        }

        UpdateEditSlidesButtonState(linked);
    }

    /// <summary>
    /// Keeps the "Edit slides" / "+ Add slides" button consistent with the current link state.
    /// Enabled only when a presentation is linked; label becomes "+ Add slides" when the linked
    /// presentation currently has zero slides (the exact situation users got lost in), else
    /// "Edit slides". This NEVER touches the dirty flag — it only reflects state.
    /// </summary>
    private void UpdateEditSlidesButtonState(SlidePresentation? linked)
    {
        bool hasLink = !string.IsNullOrEmpty(_working.SlidePresentationId);
        EditSlidesButton.IsEnabled = hasLink;

        if (!hasLink)
        {
            EditSlidesButton.Content = "Edit slides";
            EditSlidesButton.ToolTip = "Link a presentation with Select first";
            return;
        }

        EditSlidesButton.ToolTip = null;
        EditSlidesButton.Content = (linked != null && linked.Slides.Count == 0)
            ? "+ Add slides"
            : "Edit slides";
    }

    private async System.Threading.Tasks.Task PopulateSourcesAsync()
    {
        _sources.Clear();

        // (1) None — always first.
        _sources.Add(SourceRow.None());

        // (2) "Current: <type>" read-only sentinel for a non-media background.
        SourceRow? sentinel = null;
        if (_working.BackgroundType == BackgroundType.SolidColor || _working.BackgroundType == BackgroundType.Gradient)
        {
            sentinel = SourceRow.Sentinel($"Current: {_working.BackgroundType}");
            _sources.Add(sentinel);
        }

        // (3) Image/Video library rows.
        var libraryRows = new List<SourceRow>();
        try
        {
            var images = await _mediaLibraryService.GetByTypeAsync(MediaLibraryType.Image);
            foreach (var m in images) libraryRows.Add(SourceRow.Library(m));
            var videos = await _mediaLibraryService.GetByTypeAsync(MediaLibraryType.Video);
            foreach (var m in videos) libraryRows.Add(SourceRow.Library(m));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SlideServiceItemEditor] media library enumeration failed: {ex.Message}");
        }

        foreach (var row in libraryRows) _sources.Add(row);

        // Preselect which row matches the item's current state.
        SourceRow? selected;
        if (_working.BackgroundType == null)
        {
            selected = _sources[0]; // None
        }
        else if (sentinel != null)
        {
            selected = sentinel;
        }
        else
        {
            // Image/Video background: match library row by FilePath (case-insensitive).
            selected = libraryRows.FirstOrDefault(r =>
                string.Equals(r.Media!.FilePath, _working.MediaPath, StringComparison.OrdinalIgnoreCase));

            if (selected == null && !string.IsNullOrEmpty(_working.MediaPath))
            {
                // Out-of-library path: transient "Current: <file>" row so it is shown and preserved.
                var fileName = System.IO.Path.GetFileName(_working.MediaPath);
                selected = SourceRow.Sentinel($"Current: {fileName}");
                _sources.Add(selected);
            }

            selected ??= _sources[0];
        }

        SourceComboBox.SelectedItem = selected;
    }

    private async System.Threading.Tasks.Task PopulateTargetsAsync()
    {
        _targets.Clear();

        foreach (var it in _allItems)
        {
            if (ReferenceEquals(it, _live) || it.Id == _live.Id) continue;

            SlidePresentation? linked = null;
            if (!string.IsNullOrEmpty(it.SlidePresentationId))
            {
                try
                {
                    linked = await _slidePresentationService.GetByIdAsync(it.SlidePresentationId);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SlideServiceItemEditor] target presentation load failed: {ex.Message}");
                }
            }

            var label = ServiceItemDisplayName.ResolveDisplayName(it, linked);
            _targets.Add(new TargetRow(it.Id, label));
        }
    }

    private void PopulateAutoAdvanceDropdowns()
    {
        AfterComboBox.Items.Clear();
        AfterComboBox.Items.Add(SlideAutoAdvanceMap.AfterDisabled);
        AfterComboBox.Items.Add(SlideAutoAdvanceMap.AfterTimeDelay);
        AfterComboBox.Items.Add(SlideAutoAdvanceMap.AfterEndOfMedia);

        WhenCompleteComboBox.Items.Clear();
        WhenCompleteComboBox.Items.Add(SlideAutoAdvanceMap.GoToDoNothing);
        WhenCompleteComboBox.Items.Add(SlideAutoAdvanceMap.GoToNextItem);
        WhenCompleteComboBox.Items.Add(SlideAutoAdvanceMap.GoToSpecificItem);
        WhenCompleteComboBox.Items.Add(SlideAutoAdvanceMap.GoToLoop);

        var settings = _working.AutoAdvanceSettings;
        var afterLabel = settings != null ? SlideAutoAdvanceMap.AfterLabel(settings) : SlideAutoAdvanceMap.AfterDisabled;
        var goToLabel = settings != null ? SlideAutoAdvanceMap.GoToLabel(settings) : SlideAutoAdvanceMap.GoToDoNothing;

        AfterComboBox.SelectedItem = afterLabel;
        WhenCompleteComboBox.SelectedItem = goToLabel;

        DelayTextBox.Text = (settings?.AutoAdvanceDelayMs ?? 5000).ToString(CultureInfo.InvariantCulture);

        // Preselect the target item for SpecificItem.
        if (settings?.AutoAdvanceGoTo == AutoAdvanceGoTo.SpecificItem && settings.GoToItemId != null)
        {
            TargetComboBox.SelectedItem = _targets.FirstOrDefault(t => t.Id == settings.GoToItemId);
        }

        UpdateAutoAdvanceVisibility();
    }

    private void PopulateAudio()
    {
        _audio.Clear();
        foreach (var path in ServiceItemAudio.Resolve(_working))
            _audio.Add(new AudioRow(path));
        UpdateAudioEmptyState();
    }

    private void PopulateTransition()
    {
        var ms = Math.Max(0, Math.Min(3000, _working.TransitionTimeMs));
        TransitionSlider.Value = ms;
        TransitionValueText.Text = $"{ms} ms";
    }

    private async System.Threading.Tasks.Task PopulateTemplateAsync()
    {
        _selectedTemplateId = _working.StageTemplateId;

        SlideTemplate? template = null;
        if (!string.IsNullOrEmpty(_working.StageTemplateId))
        {
            try
            {
                template = await _templateService.GetByIdAsync(_working.StageTemplateId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SlideServiceItemEditor] template resolve failed: {ex.Message}");
            }
        }

        ApplyTemplateTile(template);
    }

    private void ApplyTemplateTile(SlideTemplate? template)
    {
        if (template == null)
        {
            _selectedTemplateId = null;
            TemplateLabel.Text = "No template selected";
            TemplateTileName.Text = "Aa";
            TemplateTileDescriptor.Text = "";
            ClearTemplateButton.IsEnabled = false;
            return;
        }

        _selectedTemplateId = template.Id;
        TemplateLabel.Text = template.Name;
        TemplateTileName.Text = "Aa";
        TemplateTileDescriptor.Text = DescribeTemplate(template);
        ClearTemplateButton.IsEnabled = true;
    }

    private static string DescribeTemplate(SlideTemplate t)
    {
        var weight = string.IsNullOrWhiteSpace(t.FontWeight) ? "Regular" : t.FontWeight;
        return $"{t.FontFamily} · {weight} · {t.HorizontalAlignment}";
    }

    // ─── Dirty tracking ────────────────────────────────────────────────────────────────────────

    private void MarkDirty()
    {
        if (_suppressDirty) return;
        _isDirty = true;
        UpdateSaveEnabled();
    }

    private void UpdateSaveEnabled() => SaveButton.IsEnabled = _isDirty;

    // ─── Rename (deferred to save) ───────────────────────────────────────────────────────────────

    private void Rename_Click(object sender, RoutedEventArgs e)
    {
        var current = _pendingPresentationName
            ?? (_working.SlidePresentationId is null ? _working.Title : LinkedPresentationText.Text);
        NameEditBox.Text = current;
        NameDisplayPanel.Visibility = Visibility.Collapsed;
        NameEditPanel.Visibility = Visibility.Visible;
        NameEditBox.Focus();
        NameEditBox.SelectAll();
    }

    private void RenameConfirm_Click(object sender, RoutedEventArgs e)
    {
        var entered = NameEditBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(entered))
        {
            // Reject blank — keep prior name.
            NameEditPanel.Visibility = Visibility.Collapsed;
            NameDisplayPanel.Visibility = Visibility.Visible;
            return;
        }

        if (string.IsNullOrEmpty(_working.SlidePresentationId))
        {
            // Non-linked: write the working Title (applied to _live only on save).
            _working.Title = entered;
            NameTextBlock.Text = entered;
        }
        else
        {
            // Live-linked: store pending name; no service call now. Header updates immediately.
            _pendingPresentationName = entered;
            NameTextBlock.Text = entered;
            LinkedPresentationText.Text = entered;
        }

        NameEditPanel.Visibility = Visibility.Collapsed;
        NameDisplayPanel.Visibility = Visibility.Visible;
        MarkDirty();
    }

    private void RenameCancel_Click(object sender, RoutedEventArgs e)
    {
        NameEditPanel.Visibility = Visibility.Collapsed;
        NameDisplayPanel.Visibility = Visibility.Visible;
    }

    // ─── Slides (presentation picker) ──────────────────────────────────────────────────────────

    private async void SelectPresentation_Click(object sender, RoutedEventArgs e)
    {
        List<SlidePresentation> presentations;
        try
        {
            presentations = await _slidePresentationService.GetAllAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SlideServiceItemEditor] presentation enumeration failed: {ex.Message}");
            presentations = new List<SlidePresentation>();
        }

        var rows = presentations
            .OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(p => new ListPickerDialog.PickerRow(p.Id, string.IsNullOrWhiteSpace(p.Name) ? "(unnamed)" : p.Name))
            .ToList();

        var dialog = new ListPickerDialog("SELECT A PRESENTATION", rows, _working.SlidePresentationId) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedId == null) return;

        // Select sets SlidePresentationId ONLY — no name/content snapshot.
        _working.SlidePresentationId = dialog.SelectedId;

        var chosen = presentations.FirstOrDefault(p => p.Id == dialog.SelectedId);
        _pendingPresentationName = null;
        RenameButton.IsEnabled = true;
        if (chosen != null)
        {
            LinkedPresentationText.Text = chosen.Name;
            NameTextBlock.Text = ServiceItemDisplayName.ResolveDisplayName(_working, chosen);
        }

        UpdateEditSlidesButtonState(chosen);
        MarkDirty();
    }

    /// <summary>
    /// Opens the slide CONTENT editor (<see cref="SlideEditorWindow"/>) for the CURRENTLY-LINKED
    /// presentation (resolved by id). This edits the presentation BY ID — it never snapshots slides
    /// onto the service item and never changes <see cref="ServiceItem.SlidePresentationId"/> (that
    /// is Select's job). On a successful inner save it refreshes the Slides-card display WITHOUT
    /// arming this editor's dirty flag (the inner window already persisted via UpdateAsync), then
    /// raises <see cref="LinkedPresentationSlidesEdited"/> so the host can re-prime/refresh.
    /// </summary>
    private async void EditSlides_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_working.SlidePresentationId)) return;

        SlidePresentation? presentation;
        try
        {
            presentation = await _slidePresentationService.GetByIdAsync(_working.SlidePresentationId);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SlideServiceItemEditor] edit-slides load failed: {ex.Message}");
            presentation = null;
        }

        if (presentation == null)
        {
            MessageBox.Show(this,
                "This presentation could not be found. It may have been deleted. Use Select to link another presentation.",
                "Edit Slides",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var editor = new SlideEditorWindow(presentation, _slidePresentationService) { Owner = this };
        if (editor.ShowDialog() != true) return;

        // The inner window persisted the slide content itself. Refresh our display WITHOUT marking
        // the service item dirty: re-fetch and update the Slides-card label + button, under the
        // existing _suppressDirty discipline (save/restore the prior value).
        var prevSuppress = _suppressDirty;
        _suppressDirty = true;
        try
        {
            SlidePresentation? refetched = null;
            try
            {
                refetched = await _slidePresentationService.GetByIdAsync(_working.SlidePresentationId);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SlideServiceItemEditor] edit-slides re-fetch failed: {ex.Message}");
            }

            if (refetched != null)
            {
                if (_pendingPresentationName == null)
                {
                    LinkedPresentationText.Text = refetched.Name;
                    NameTextBlock.Text = ServiceItemDisplayName.ResolveDisplayName(_working, refetched);
                }
                UpdateEditSlidesButtonState(refetched);
            }
        }
        finally
        {
            _suppressDirty = prevSuppress;
        }

        // Let the host re-prime its cache / refresh the center grid so added slides show now.
        LinkedPresentationSlidesEdited?.Invoke(this, EventArgs.Empty);
    }

    // ─── Background source ───────────────────────────────────────────────────────────────────────

    private void SourceComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => MarkDirty();

    // ─── Auto-advance ────────────────────────────────────────────────────────────────────────────

    private void AfterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAutoAdvanceVisibility();
        MarkDirty();
    }

    private void WhenCompleteComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAutoAdvanceVisibility();
        MarkDirty();
    }

    private void TargetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => MarkDirty();

    private void DelayTextBox_TextChanged(object sender, TextChangedEventArgs e) => MarkDirty();

    private void UpdateAutoAdvanceVisibility()
    {
        var afterLabel = AfterComboBox.SelectedItem as string ?? SlideAutoAdvanceMap.AfterDisabled;
        var goToLabel = WhenCompleteComboBox.SelectedItem as string ?? SlideAutoAdvanceMap.GoToDoNothing;

        WhenCompleteComboBox.IsEnabled = SlideAutoAdvanceMap.WhenCompleteEnabled(afterLabel);

        DelayPanel.Visibility = afterLabel == SlideAutoAdvanceMap.AfterTimeDelay
            ? Visibility.Visible : Visibility.Collapsed;

        TargetPanel.Visibility = (WhenCompleteComboBox.IsEnabled && goToLabel == SlideAutoAdvanceMap.GoToSpecificItem)
            ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─── Transitions ───────────────────────────────────────────────────────────────────────────

    private void TransitionSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var ms = (int)Math.Round(e.NewValue);
        TransitionValueText.Text = $"{ms} ms";
        _working.TransitionTimeMs = ms;
        MarkDirty();
    }

    // ─── Audio ─────────────────────────────────────────────────────────────────────────────────

    private void AddAudio_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Audio|*.mp3;*.wav;*.m4a;*.aac;*.flac;*.ogg|All files|*.*",
            Multiselect = false
        };

        if (dialog.ShowDialog(this) != true) return;

        _audio.Add(new AudioRow(dialog.FileName));
        UpdateAudioEmptyState();
        MarkDirty();
    }

    private void RemoveAudio_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string path })
        {
            var row = _audio.FirstOrDefault(a => a.Path == path);
            if (row != null)
            {
                _audio.Remove(row);
                UpdateAudioEmptyState();
                MarkDirty();
            }
        }
    }

    private void UpdateAudioEmptyState()
    {
        AudioEmptyText.Visibility = _audio.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ─── Stage display template ──────────────────────────────────────────────────────────────────

    private async void SelectTemplate_Click(object sender, RoutedEventArgs e)
    {
        List<SlideTemplate> templates;
        try
        {
            templates = await _templateService.GetAllAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SlideServiceItemEditor] template enumeration failed: {ex.Message}");
            templates = new List<SlideTemplate>();
        }

        var rows = templates
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(t => new ListPickerDialog.PickerRow(t.Id, string.IsNullOrWhiteSpace(t.Name) ? "(unnamed)" : t.Name))
            .ToList();

        var dialog = new ListPickerDialog("SELECT A TEMPLATE", rows, _selectedTemplateId) { Owner = this };
        if (dialog.ShowDialog() != true || dialog.SelectedId == null) return;

        var chosen = templates.FirstOrDefault(t => t.Id == dialog.SelectedId);
        ApplyTemplateTile(chosen);
        MarkDirty();
    }

    private void ClearTemplate_Click(object sender, RoutedEventArgs e)
    {
        ApplyTemplateTile(null);
        MarkDirty();
    }

    // ─── Remove / Close / Save ───────────────────────────────────────────────────────────────────

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        // Raise ONLY. The host owns closing; no Result, no DialogResult, no Close, no manager call.
        RemoveRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        // Discard: no copy-back, no deferred rename applied.
        DialogResult = false;
        Close();
    }

    private async void SaveAndClose_Click(object sender, RoutedEventArgs e)
    {
        if (_saving) return;
        _saving = true;

        try
        {
            // (1) Apply a deferred linked-presentation rename (skip-only on failure/missing).
            if (_pendingPresentationName != null && !string.IsNullOrEmpty(_working.SlidePresentationId))
            {
                try
                {
                    var linked = await _slidePresentationService.GetByIdAsync(_working.SlidePresentationId);
                    if (linked != null)
                    {
                        linked.Name = _pendingPresentationName;
                        await _slidePresentationService.UpdateAsync(linked);
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SlideServiceItemEditor] deferred rename failed: {ex.Message}");
                    MessageBox.Show(this,
                        $"Failed to rename the presentation: {ex.Message}",
                        "Rename Presentation",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
            }

            // (2) Map the two Auto-advance dropdowns into _working.AutoAdvanceSettings.
            ApplyAutoAdvance();

            // (3) Map the Source selection into background fields.
            ApplyBackgroundSource();

            // (4) Write audio list + keep legacy single path in sync.
            var audioPaths = _audio.Select(a => a.Path).ToList();
            _working.AudioAttachments = audioPaths.Count > 0 ? audioPaths : null;
            _working.AudioAttachmentPath = _working.AudioAttachments?.FirstOrDefault() ?? "";

            // Template id from the UI state.
            _working.StageTemplateId = _selectedTemplateId;

            // (5) Copy the ENUMERATED edited fields back onto _live IN PLACE.
            _live.SlidePresentationId = _working.SlidePresentationId;
            _live.BackgroundType = _working.BackgroundType;
            _live.MediaPath = _working.MediaPath;
            _live.MediaType = _working.MediaType;
            _live.TransitionTimeMs = _working.TransitionTimeMs;
            _live.AudioAttachments = _working.AudioAttachments;
            _live.AudioAttachmentPath = _working.AudioAttachmentPath;
            _live.StageTemplateId = _working.StageTemplateId;
            _live.AutoAdvanceSettings = _working.AutoAdvanceSettings;

            // Title is copied back ONLY for a non-linked rename.
            if (string.IsNullOrEmpty(_working.SlidePresentationId))
                _live.Title = _working.Title;

            // (6) Result + DialogResult.
            Result = _live;
            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            _saving = false;
            MessageBox.Show(this,
                $"Failed to save the service item: {ex.Message}",
                "Save Service Item",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Map the two Auto-advance dropdowns onto <see cref="ServiceItem.AutoAdvanceSettings"/> via
    /// <see cref="SlideAutoAdvanceMap"/>. Leaves AutoAdvanceSettings null for the canonical
    /// Disabled + Do-nothing off state when no settings object already exists.
    /// </summary>
    private void ApplyAutoAdvance()
    {
        var afterLabel = AfterComboBox.SelectedItem as string ?? SlideAutoAdvanceMap.AfterDisabled;
        var goToLabel = WhenCompleteComboBox.SelectedItem as string ?? SlideAutoAdvanceMap.GoToDoNothing;
        var targetId = (TargetComboBox.SelectedItem as TargetRow)?.Id;

        bool isCanonicalOff = afterLabel == SlideAutoAdvanceMap.AfterDisabled
            && goToLabel == SlideAutoAdvanceMap.GoToDoNothing;

        if (isCanonicalOff && _working.AutoAdvanceSettings == null)
        {
            // Nothing to fabricate — keep the off state.
            return;
        }

        var settings = _working.AutoAdvanceSettings ?? new ServiceItemSettings();
        SlideAutoAdvanceMap.SaveAfter(settings, afterLabel);
        SlideAutoAdvanceMap.SaveWhenComplete(settings, goToLabel, targetId);

        if (afterLabel == SlideAutoAdvanceMap.AfterTimeDelay
            && int.TryParse(DelayTextBox.Text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var delay)
            && delay >= 0)
        {
            settings.AutoAdvanceDelayMs = delay;
        }

        // Keep the settings-object transition copy consistent with the slider-bound field.
        settings.TransitionFadeTimeMs = _working.TransitionTimeMs;

        _working.AutoAdvanceSettings = settings;
    }

    /// <summary>
    /// Map the Source selection into <see cref="ServiceItem.BackgroundType"/>/MediaPath/MediaType
    /// per design Gap #3: None clears; the "Current" sentinel writes nothing; a library row sets all three.
    /// </summary>
    private void ApplyBackgroundSource()
    {
        if (SourceComboBox.SelectedItem is not SourceRow row) return;

        switch (row.Kind)
        {
            case SourceKind.None:
                _working.BackgroundType = null;
                _working.MediaPath = "";
                break;

            case SourceKind.Sentinel:
                // Preserve the existing background fields untouched.
                break;

            case SourceKind.Library:
                var media = row.Media!;
                _working.MediaPath = media.FilePath;
                _working.MediaType = media.MediaType == MediaLibraryType.Video ? MediaItemType.Video : MediaItemType.Image;
                _working.BackgroundType = media.MediaType == MediaLibraryType.Video ? BackgroundType.Video : BackgroundType.Image;
                break;
        }
    }

    // ─── Row view-models ─────────────────────────────────────────────────────────────────────────

    private enum SourceKind { None, Sentinel, Library }

    private sealed class SourceRow
    {
        private SourceRow(SourceKind kind, string label, MediaLibraryItem? media)
        {
            Kind = kind;
            Label = label;
            Media = media;
        }

        public SourceKind Kind { get; }
        public string Label { get; }
        public MediaLibraryItem? Media { get; }

        public static SourceRow None() => new(SourceKind.None, "None", null);
        public static SourceRow Sentinel(string label) => new(SourceKind.Sentinel, label, null);
        public static SourceRow Library(MediaLibraryItem media) =>
            new(SourceKind.Library, string.IsNullOrWhiteSpace(media.Name) ? System.IO.Path.GetFileName(media.FilePath) : media.Name, media);
    }

    private sealed class TargetRow
    {
        public TargetRow(string id, string label)
        {
            Id = id;
            Label = label;
        }

        public string Id { get; }
        public string Label { get; }
    }

    private sealed class AudioRow
    {
        public AudioRow(string path) => Path = path;
        public string Path { get; }
    }
}
