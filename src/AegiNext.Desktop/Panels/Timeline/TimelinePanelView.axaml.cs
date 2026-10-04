using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Localization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace AegiNext.Desktop.Panels.Timeline;

internal sealed partial class TimelinePanelView : UserControl, IWorkbenchPanelView
{
    private readonly WorkbenchSession session;
    private readonly TimelinePanelViewModel viewModel;
    private readonly SubtitleTimelineControl timeline;
    private readonly TimelineOverviewControl overview;
    private readonly MenuItem collapseTrackItem;
    private AegiNext.Media.Analysis.SpectrogramData? spectrum;
    private bool disposed;
    private bool applying;
    internal TimelinePanelView(TimelinePanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        timeline = this.FindControl<SubtitleTimelineControl>("Timeline")!;
        overview = this.FindControl<TimelineOverviewControl>("TimelineMinimap")!;
        TrackMenu = new();
        TrackMenu.Items.Add(new MenuItem { Name = "AddSubtitleTrackMenuItem", Tag = "AddTrack", Command = viewModel.AddTrackCommand });
        TrackMenu.Items.Add(new MenuItem { Name = "RenameSubtitleTrackMenuItem", Tag = "RenameTrack", Command = viewModel.RenameTrackCommand });
        TrackMenu.Items.Add(new MenuItem { Name = "DeleteSubtitleTrackMenuItem", Tag = "DeleteTrack", Command = viewModel.DeleteTrackCommand });
        TrackMenu.Items.Add(new Separator());
        TrackMenu.Items.Add(new MenuItem { Name = "MoveSubtitleTrackUpMenuItem", Tag = "MoveTrackUp", Command = viewModel.MoveTrackUpCommand });
        TrackMenu.Items.Add(new MenuItem { Name = "MoveSubtitleTrackDownMenuItem", Tag = "MoveTrackDown", Command = viewModel.MoveTrackDownCommand });
        collapseTrackItem = new() { Name = "CollapseSubtitleTrackMenuItem" };
        collapseTrackItem.Click += (_, _) =>
        {
            if (viewModel.SelectedTrackId is { } id)
            {
                timeline.ToggleTrackCollapse(id);
            }
        };
        TrackMenu.Items.Add(collapseTrackItem);
        timeline.TrackContextRequested += OnTrackContextRequested;
        timeline.SeekRequested += async (_, e) => await viewModel.SeekAsync(e.Time);
        timeline.ClipSelectionChanged += (_, e) => viewModel.SelectLayers(e);
        timeline.TrackSelected += (_, e) => viewModel.SelectTrack(e.Id);
        timeline.ViewportChanged += OnViewportChanged;
        overview.ViewportChanged += OnViewportChanged;
        timeline.TimingChanged += async (_, e) => await viewModel.CommitTimingAsync(e);
        timeline.KeyframeSelected += (_, e) => e.SelectionAccepted = viewModel.SelectKeyframe(e);
        timeline.KeyframeMoved += async (_, e) => await viewModel.MoveKeyframeAsync(e);
        timeline.AddHandler(PointerPressedEvent, (_, _) => viewModel.IsSeeking = timeline.IsSeeking, RoutingStrategies.Bubble, true);
        timeline.AddHandler(PointerReleasedEvent, (_, _) => viewModel.IsSeeking = false, RoutingStrategies.Bubble, true);
        timeline.AddHandler(PointerCaptureLostEvent, (_, _) => viewModel.IsSeeking = false, RoutingStrategies.Bubble, true);
        timeline.SizeChanged += (_, _) =>
        {
            viewModel.RefreshViewport();
        };
        viewModel.PropertyChanged += OnViewModelChanged;
        session.PreferencesChanged += OnPreferencesChanged;
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        ApplyState();
        ControlLocalization.Apply(this);
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
            timeline.EffectProperty = viewModel.EffectProperty;
            timeline.SetDocument(viewModel.Document, viewModel.SelectedCueId, viewModel.SelectedLayer,
                viewModel.SelectedLayerIds.Count == 0 && viewModel.SelectedLayer is { } selected ? [selected.Id] : viewModel.SelectedLayerIds,
                viewModel.SelectedTrackId);
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
        TrackMenu.Close();
        if (e.TrackId is { } id && !viewModel.SelectTrack(id))
        {
            return;
        }

        RefreshTrackMenu();
        TrackMenu.Open(timeline);
    }
    private void RefreshTrackMenu()
    {
        foreach (var item in TrackMenu.Items.OfType<MenuItem>().Where(item => item.Tag is string))
        {
            item.Header = WorkbenchText.Get((string)item.Tag!);
        }

        collapseTrackItem.IsEnabled = viewModel.SelectedTrackId.HasValue;
        collapseTrackItem.Header = WorkbenchText.Get(viewModel.SelectedTrackId is { } id && timeline.IsTrackCollapsed(id)
            ? "ExpandTrack" : "CollapseTrack");
    }
    private void OnPreferencesChanged(object? sender, EventArgs e)
    {
        ControlLocalization.Apply(this);
        RefreshTrackMenu();
    }
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            viewModel.PropertyChanged -= OnViewModelChanged;
            session.PreferencesChanged -= OnPreferencesChanged;
            session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
            timeline.ViewportChanged -= OnViewportChanged;
            overview.ViewportChanged -= OnViewportChanged;
            timeline.TrackContextRequested -= OnTrackContextRequested;
            TrackMenu.Close();
            overview.CancelGesture();
            timeline.Dispose();
        }
    }
}
