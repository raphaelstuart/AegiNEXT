using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using System.Windows.Input;
using Avalonia;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.I18n;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AegiNext.Desktop.Styling;
using Material.Icons.Avalonia;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Windowing;

namespace AegiNext.Desktop.Panels.Timeline;

internal sealed partial class TimelinePanelView : UserControl, IWorkbenchPanelView, IWorkbenchFocusCommandTarget
{
    private readonly WorkbenchSession session;
    private readonly TimelinePanelViewModel viewModel;
    private readonly SubtitleTimelineControl timeline;
    private readonly TimelineOverviewControl overview;
    private readonly MenuItem collapseTrackItem;
    private readonly MenuItem trackStyleItem;
    private readonly MenuItem autoTrackStyleItem;
    private readonly ToolbarToggleButton snapButton;
    private readonly ToolbarToggleButton stepButton;
    private readonly ToolbarToggleButton spectrumButton;
    private readonly ToolbarToggleButton waveformButton;
    private AegiNext.Media.Analysis.SpectrogramData? spectrum;
    private IPointer? animationRowCollapsePointer;
    private bool disposed;
    private bool applying;
    internal TimelinePanelView(TimelinePanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        timeline = this.FindControl<SubtitleTimelineControl>("Timeline")!;
        timeline.SetAudioGraphPalette(session.Preferences.AudioGraph);
        timeline.SetClipPalette(session.Preferences.TimelineClips);
        overview = this.FindControl<TimelineOverviewControl>("TimelineMinimap")!;
        snapButton = this.FindControl<ToolbarToggleButton>("TimelineSnapButton")!;
        stepButton = this.FindControl<ToolbarToggleButton>("TimelineStepButton")!;
        spectrumButton = this.FindControl<ToolbarToggleButton>("TimelineSpectrumButton")!;
        waveformButton = this.FindControl<ToolbarToggleButton>("TimelineWaveformButton")!;
        this.FindControl<MaterialIcon>("TimelineSnapIcon")!.Kind = WorkbenchIcon.ResolveKind("Magnet");
        this.FindControl<MaterialIcon>("TimelineSpectrumIcon")!.Kind = WorkbenchIcon.ResolveKind("Spectrum");
        this.FindControl<MaterialIcon>("TimelineWaveformIcon")!.Kind = WorkbenchIcon.ResolveKind("Waveform");
        TrackMenu = new();
        TrackMenu.Items.Add(CreateMenuItem("AddSubtitleTrackMenuItem", "AddTrack", viewModel.AddTrackCommand));
        TrackMenu.Items.Add(CreateMenuItem("RenameSubtitleTrackMenuItem", "RenameTrack", viewModel.RenameTrackCommand));
        TrackMenu.Items.Add(CreateMenuItem("DeleteSubtitleTrackMenuItem", "DeleteTrack", viewModel.DeleteTrackCommand));
        TrackMenu.Items.Add(new Separator());
        TrackMenu.Items.Add(CreateMenuItem("MoveSubtitleTrackUpMenuItem", "MoveTrackUp", viewModel.MoveTrackUpCommand));
        TrackMenu.Items.Add(CreateMenuItem("MoveSubtitleTrackDownMenuItem", "MoveTrackDown", viewModel.MoveTrackDownCommand));
        collapseTrackItem = new() { Name = "CollapseSubtitleTrackMenuItem" };
        collapseTrackItem.Click += (_, _) =>
        {
            if (viewModel.SelectedTrackId is { } id)
            {
                timeline.ToggleTrackCollapse(id);
            }
        };
        TrackMenu.Items.Add(collapseTrackItem);
        TrackMenu.Items.Add(new Separator());
        trackStyleItem = CreateMenuItem("SubtitleTrackStyleMenuItem", "TrackSubtitleStyle");
        autoTrackStyleItem = new()
        {
            Name = "AutoApplySubtitleTrackStyleMenuItem", ToggleType = MenuItemToggleType.CheckBox,
            Command = viewModel.ToggleTrackAutoStyleCommand
        };
        autoTrackStyleItem.Bind(MenuItem.HeaderProperty, Localization.Observe("Workbench.TrackStyleAutoApply").ToBinding());
        TrackMenu.Items.Add(trackStyleItem);
        TrackMenu.Items.Add(autoTrackStyleItem);
        ClipMenu = new();
        ClipMenu.Items.Add(CreateMenuItem("CreateTimelineSubtitleMenuItem", "CreateTimelineSubtitle", viewModel.CreateSubtitleCommand));
        ClipMenu.Items.Add(new Separator());
        ClipMenu.Items.Add(CreateMenuItem("CopyTimelineClipsMenuItem", "CopyTimelineClips", viewModel.CopyClipsCommand));
        ClipMenu.Items.Add(CreateMenuItem("PasteTimelineClipsMenuItem", "PasteTimelineClips", viewModel.PasteClipsCommand));
        ClipMenu.Items.Add(CreateMenuItem("DeleteTimelineClipsMenuItem", "DeleteTimelineClips", viewModel.DeleteClipsCommand));
        timeline.TrackContextRequested += OnTrackContextRequested;
        timeline.ClipContextRequested += OnClipContextRequested;
        timeline.SeekRequested += async (_, e) => await viewModel.SeekAsync(e.Time);
        timeline.ClipSelectionChanged += (_, e) => e.SelectionAccepted = viewModel.SelectLayers(e);
        timeline.TrackSelected += (_, e) => viewModel.SelectTrack(e.Id);
        timeline.ViewportChanged += OnViewportChanged;
        overview.ViewportChanged += OnViewportChanged;
        timeline.TimingChanged += async (_, e) => await viewModel.CommitTimingAsync(e);
        timeline.ClipsMoveCompleted += async (_, e) => await viewModel.CommitClipsMoveAsync(e);
        timeline.KeyframeSelected += (_, e) => e.SelectionAccepted = viewModel.SelectKeyframe(e);
        timeline.KeyframeMoved += async (_, e) => await viewModel.MoveKeyframeAsync(e);
        timeline.AnimationRowCollapseRequested += OnAnimationRowCollapseRequested;
        AddHandler(PointerPressedEvent, OnPreviewPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnPreviewPointerReleased, RoutingStrategies.Tunnel);
        timeline.AddHandler(PointerPressedEvent, (_, _) => viewModel.IsSeeking = timeline.IsSeeking, RoutingStrategies.Bubble, true);
        timeline.AddHandler(PointerReleasedEvent, (_, _) => viewModel.IsSeeking = false, RoutingStrategies.Bubble, true);
        timeline.AddHandler(PointerCaptureLostEvent, (_, _) => viewModel.IsSeeking = false, RoutingStrategies.Bubble, true);
        timeline.SizeChanged += (_, _) =>
        {
            viewModel.RefreshViewport();
        };
        viewModel.PropertyChanged += OnViewModelChanged;
        session.PreferencesChanged += OnPreferencesChanged;
        Localization.LanguageChanged += OnLanguageChanged;
        session.StyleLibraryChanged += OnStyleLibraryChanged;
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        ApplyState();
        RefreshTrackMenu();
        var nameInput = this.FindControl<TextBox>("TimelineTrackNameInput")!;
        nameInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                viewModel.CancelTrackRenameCommand.Execute(null);
                timeline.Focus();
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && viewModel.ConfirmTrackRenameCommand.CanExecute(null))
            {
                viewModel.ConfirmTrackRenameCommand.Execute(null);
                e.Handled = true;
            }
        };
    }

    public string PanelId => "timeline";
    internal ContextMenu TrackMenu { get; }
    internal ContextMenu ClipMenu { get; }
    public bool CanExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (!ReferenceEquals(focusedElement, timeline) || TrackMenu.IsOpen || ClipMenu.IsOpen)
        {
            return false;
        }
        return command switch
        {
            WorkbenchCommand.AUDITION_BEFORE_SUBTITLE or WorkbenchCommand.AUDITION_AFTER_SUBTITLE or
                WorkbenchCommand.AUDITION_SUBTITLE_BEGIN or WorkbenchCommand.AUDITION_SUBTITLE => session.CanAuditionSubtitle && !timeline.HasActiveDrag,
            WorkbenchCommand.END_TEXT_INPUT => timeline.HasActiveDrag,
            WorkbenchCommand.COPY_CLIPS or WorkbenchCommand.DELETE_SUBTITLE => viewModel.CanCopyClips,
            WorkbenchCommand.PASTE_CLIPS => viewModel.CanPasteClips && timeline.GetClipPasteTarget() is not null,
            _ => false
        };
    }

    public bool TryExecuteFocusCommand(WorkbenchCommand command, IInputElement focusedElement)
    {
        if (!CanExecuteFocusCommand(command, focusedElement))
        {
            return false;
        }
        switch (command)
        {
            case WorkbenchCommand.AUDITION_BEFORE_SUBTITLE:
            case WorkbenchCommand.AUDITION_AFTER_SUBTITLE:
            case WorkbenchCommand.AUDITION_SUBTITLE_BEGIN:
            case WorkbenchCommand.AUDITION_SUBTITLE:
                _ = session.ExecuteCommandAsync(command);
                break;
            case WorkbenchCommand.END_TEXT_INPUT:
                CancelGestures();
                break;
            case WorkbenchCommand.COPY_CLIPS:
                _ = viewModel.CopySelectedClipsAsync();
                break;
            case WorkbenchCommand.PASTE_CLIPS:
                if (timeline.GetClipPasteTarget() is not { } target)
                {
                    return false;
                }
                _ = viewModel.PasteSelectedClipsAsync(target);
                break;
            case WorkbenchCommand.DELETE_SUBTITLE:
                _ = viewModel.DeleteSelectedClipsAsync();
                break;
        }
        return true;
    }
    public void CancelGestures()
    {
        timeline.CancelGesture();
        overview.CancelGesture();
        viewModel.IsSeeking = false;
    }
    public void FocusInvalidField(string? fieldKey) => timeline.Focus();
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        ApplyState();
        if (e.PropertyName is nameof(viewModel.Document) or nameof(viewModel.SelectedTrackId))
        {
            RefreshTrackMenu();
        }
        if (e.PropertyName == nameof(viewModel.IsRenamingTrack) && viewModel.IsRenamingTrack)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!disposed && viewModel.IsRenamingTrack)
                {
                    var input = this.FindControl<TextBox>("TimelineTrackNameInput")!;
                    input.Focus();
                    input.SelectAll();
                }
            });
        }
    }
    private void OnAnimationRowCollapseRequested(object? sender, TimelineAnimationRowCollapseEventArgs e) =>
        viewModel.SetAnimationRowCollapsed(e);

    private void OnPreviewPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (ReferenceEquals(animationRowCollapsePointer, e.Pointer))
        {
            animationRowCollapsePointer = null;
        }

        if (ReferenceEquals(e.Source, timeline) && e.GetCurrentPoint(timeline).Properties.IsLeftButtonPressed &&
            timeline.TryRequestAnimationRowCollapse(e.GetPosition(timeline)))
        {
            animationRowCollapsePointer = e.Pointer;
            e.Handled = true;
        }
    }

    private void OnPreviewPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (ReferenceEquals(animationRowCollapsePointer, e.Pointer))
        {
            animationRowCollapsePointer = null;
            e.Handled = true;
        }
    }

    private void ApplyState()
    {
        if (applying || disposed)
        {
            return;
        }

        applying = true;
        try
        {
            timeline.Position = viewModel.Position;
            timeline.EffectTarget = viewModel.EffectTarget;
            timeline.SelectedMaskNodeId = viewModel.SelectedMaskNodeId;
            timeline.IsSnapEnabled = viewModel.IsSnapEnabled;
            timeline.IsStepEnabled = viewModel.IsStepEnabled;
            timeline.IsSpectrumVisible = viewModel.IsSpectrumVisible;
            timeline.IsWaveformVisible = viewModel.IsWaveformVisible;
            timeline.SetDocument(viewModel.Document, viewModel.SelectedCueId, viewModel.SelectedLayer,
                viewModel.SelectedLayerIds.Count == 0 && viewModel.SelectedLayer is { } selected ? [selected.Id] : viewModel.SelectedLayerIds,
                viewModel.SelectedTrackId);
            timeline.TimelineViewState = viewModel.TimelineViewState;
            timeline.SetViewport(viewModel.Viewport, viewModel.FullDuration);
            if (!ReferenceEquals(spectrum, viewModel.Spectrogram))
            {
                spectrum = viewModel.Spectrogram;
                timeline.SetSpectrogram(spectrum);
            }

            viewModel.Viewport = timeline.Viewport;
            overview.SetScene(viewModel.Document, viewModel.Viewport, viewModel.FullDuration, viewModel.Position);
        }
        finally
        {
            applying = false;
        }
    }
    private void OnViewportChanged(object? sender, TimelineViewportEventArgs e)
    {
        viewModel.Viewport = e.Viewport;
        if (!applying)
        {
            ApplyState();
        }
    }
    private void OnTrackContextRequested(object? sender, TimelineTrackContextEventArgs e)
    {
        ClipMenu.Close();
        TrackMenu.Close();
        if (e.TrackId is { } id && !viewModel.SelectTrack(id))
        {
            return;
        }

        RefreshTrackMenu();
        TrackMenu.Open(timeline);
    }
    private void OnClipContextRequested(object? sender, TimelineClipContextEventArgs e)
    {
        TrackMenu.Close();
        ClipMenu.Close();
        viewModel.SetClipContext(e);
        ClipMenu.Open(timeline);
    }
    private void RefreshTrackMenu()
    {
        collapseTrackItem.IsEnabled = viewModel.SelectedTrackId.HasValue;
        collapseTrackItem.Header = Localization.Get("Workbench." + (viewModel.SelectedTrackId is { } id && timeline.IsTrackCollapsed(id)
            ? "ExpandTrack" : "CollapseTrack"));
        RefreshStylePresets();
    }
    private void RefreshStylePresets()
    {
        trackStyleItem.Items.Clear();
        var presets = viewModel.StylePresets;
        trackStyleItem.IsEnabled = viewModel.SelectedTrackId.HasValue && presets.Length > 0;
        var currentTrack = viewModel.Document.SubtitleTracks.FirstOrDefault(track => track.Id == viewModel.SelectedTrackId);
        autoTrackStyleItem.IsEnabled = currentTrack is not null;
        autoTrackStyleItem.IsChecked = currentTrack?.AutoApplyStyle == true;
        autoTrackStyleItem.CommandParameter = currentTrack?.Id;
        foreach (var preset in presets)
        {
            if (currentTrack is null)
            {
                break;
            }
            trackStyleItem.Items.Add(new MenuItem
            {
                Header = preset.Name,
                ToggleType = MenuItemToggleType.CheckBox,
                IsChecked = currentTrack.StylePresetId == preset.Id,
                Command = viewModel.ApplyTrackStyleCommand,
                CommandParameter = new TrackStylePresetRequest(currentTrack.Id, preset.Id)
            });
        }

        ToolTip.SetTip(trackStyleItem, presets.Length == 0 ? Localization.Get("Workbench.NoStylePresets") : currentTrack?.StylePresetName);
    }
    private void OnStyleLibraryChanged(object? sender, EventArgs e)
    {
        if (!disposed)
        {
            RefreshTrackMenu();
        }
    }
    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        timeline.SetAudioGraphPalette(session.Preferences.AudioGraph);
        timeline.SetClipPalette(session.Preferences.TimelineClips);
        timeline.InvalidateVisual();
    }
    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        RefreshTrackMenu();
    }
    private static MenuItem CreateMenuItem(string name, string key, ICommand? command = null)
    {
        var item = new MenuItem { Name = name, Command = command };
        item.Bind(MenuItem.HeaderProperty, Localization.Observe("Workbench." + key).ToBinding());
        return item;
    }
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            viewModel.PropertyChanged -= OnViewModelChanged;
            session.PreferencesChanged -= OnPreferencesChanged;
            Localization.LanguageChanged -= OnLanguageChanged;
            session.StyleLibraryChanged -= OnStyleLibraryChanged;
            session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
            timeline.ViewportChanged -= OnViewportChanged;
            overview.ViewportChanged -= OnViewportChanged;
            timeline.TrackContextRequested -= OnTrackContextRequested;
            timeline.ClipContextRequested -= OnClipContextRequested;
            timeline.AnimationRowCollapseRequested -= OnAnimationRowCollapseRequested;
            RemoveHandler(PointerPressedEvent, OnPreviewPointerPressed);
            RemoveHandler(PointerReleasedEvent, OnPreviewPointerReleased);
            animationRowCollapsePointer = null;
            TrackMenu.Close();
            ClipMenu.Close();
            overview.CancelGesture();
            timeline.Dispose();
        }
    }
}
