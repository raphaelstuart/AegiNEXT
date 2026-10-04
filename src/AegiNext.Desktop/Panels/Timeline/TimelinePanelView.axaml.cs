using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Panels.Timeline;

internal sealed partial class TimelinePanelView : UserControl, IWorkbenchPanelView
{
    private readonly WorkbenchSession session;
    private readonly TimelinePanelViewModel viewModel;
    private readonly SubtitleTimelineControl timeline;
    private readonly TimelineOverviewControl overview;
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
        timeline.SeekRequested += async (_, e) => await viewModel.SeekAsync(e.Time);
        timeline.ClipSelectionChanged += (_, e) => viewModel.SelectLayers(e);
        timeline.TrackSelected += (_, e) => viewModel.SelectTrack(e.Id);
        timeline.ViewportChanged += OnViewportChanged;
        overview.ViewportChanged += OnViewportChanged;
        timeline.TimingChanged += async (_, e) => await viewModel.CommitTimingAsync(e);
        timeline.KeyframeSelected += (_, e) => viewModel.SelectKeyframe(e);
        timeline.KeyframeMoved += async (_, e) => await viewModel.MoveKeyframeAsync(e);
        timeline.PointerPressed += (_, _) => viewModel.IsSeeking = timeline.IsSeeking;
        timeline.PointerReleased += (_, _) => viewModel.IsSeeking = false;
        timeline.PointerCaptureLost += (_, _) => viewModel.IsSeeking = false;
        timeline.SizeChanged += (_, _) =>
        {
            viewModel.RefreshViewport();
        };
        viewModel.PropertyChanged += OnViewModelChanged;
        session.PreferencesChanged += OnPreferencesChanged;
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        ApplyState();
        ControlLocalization.Apply(this);
    }

    public string PanelId => "timeline";
    public void CancelGestures()
    {
        timeline.CancelGesture();
        overview.CancelGesture();
        viewModel.IsSeeking = false;
    }
    public void FocusInvalidField(string? fieldKey) => timeline.Focus();
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => ApplyState();
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
                viewModel.SelectedLayerIds.Count == 0 && viewModel.SelectedLayer is { } selected ? [selected.Id] : viewModel.SelectedLayerIds);
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
    private void OnPreferencesChanged(object? sender, EventArgs e) => ControlLocalization.Apply(this);
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
            overview.CancelGesture();
            timeline.Dispose();
        }
    }
}
