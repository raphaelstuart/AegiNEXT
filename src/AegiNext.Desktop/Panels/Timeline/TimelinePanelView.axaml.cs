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
    private AegiNext.Media.Analysis.SpectrogramData? spectrum;
    private bool disposed;
    internal TimelinePanelView(TimelinePanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        timeline = this.FindControl<SubtitleTimelineControl>("Timeline")!;
        timeline.SeekRequested += async (_, e) => await viewModel.SeekAsync(e.Time);
        timeline.CueSelected += (_, e) => viewModel.SelectCue(e.Id);
        timeline.TimingChanged += async (_, e) => await viewModel.CommitTimingAsync(e);
        timeline.KeyframeSelected += (_, e) => viewModel.SelectKeyframe(e);
        timeline.KeyframeMoved += async (_, e) => await viewModel.MoveKeyframeAsync(e);
        timeline.PointerPressed += (_, _) => viewModel.IsSeeking = timeline.IsSeeking;
        timeline.PointerReleased += (_, _) => viewModel.IsSeeking = false;
        timeline.PointerCaptureLost += (_, _) => viewModel.IsSeeking = false;
        timeline.SizeChanged += (_, _) =>
        {
            viewModel.VisibleDuration = timeline.VisibleDuration;
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
        viewModel.IsSeeking = false;
    }
    public void FocusInvalidField(string? fieldKey) => timeline.Focus();
    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e) => ApplyState();
    private void ApplyState()
    {
        timeline.Position = viewModel.Position;
        timeline.PixelsPerSecond = viewModel.PixelsPerSecond;
        timeline.ViewStart = viewModel.ViewStart;
        timeline.ShowEffects = viewModel.ShowEffects;
        timeline.EffectProperty = viewModel.EffectProperty;
        timeline.SetDocument(viewModel.Document, viewModel.SelectedCueId, viewModel.SelectedLayer);
        if (!ReferenceEquals(spectrum, viewModel.Spectrogram))
        {
            spectrum = viewModel.Spectrogram;
            timeline.SetSpectrogram(spectrum);
        }
        viewModel.VisibleDuration = Math.Max(0.01, timeline.VisibleDuration);
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
            timeline.Dispose();
        }
    }
}
