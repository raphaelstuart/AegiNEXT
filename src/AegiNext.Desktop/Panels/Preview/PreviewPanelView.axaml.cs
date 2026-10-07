using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Styling;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Rendering;
using Avalonia;
using AegiNext.Core.Timing;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Panels.Preview;

internal sealed partial class PreviewPanelView : UserControl, IWorkbenchPanelView
{
    private readonly WorkbenchSession session;
    private readonly PreviewPanelViewModel viewModel;
    private readonly EffectCanvasControl canvas;
    private readonly Slider positionSlider;
    private double synchronizedPosition;
    private bool disposed;
    internal PreviewPanelView(PreviewPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        canvas = this.FindControl<EffectCanvasControl>("EffectCanvas")!;
        ApplyTransportIcons();
        canvas.GestureStarting += (_, e) => e.Cancel = !viewModel.BeginCanvasGesture();
        canvas.GestureCancelled += (_, _) => viewModel.CancelCanvasGesture();
        canvas.MaskGestureStarting += (_, e) => e.Cancel = !session.MaskEditing.BeginGesture();
        canvas.MaskGestureCancelled += (_, _) => session.MaskEditing.CancelGesture();
        canvas.MaskEdited += (_, e) => session.MaskEditing.CommitGesture(e);
        canvas.MaskNodeSelected += (_, e) => session.MaskEditing.SelectGestureNode(e.NodeId);
        canvas.MaskNodeDeleteRequested += (_, e) => session.MaskEditing.CommitNodeDeletion(e);
        canvas.MaskSegmentInsertRequested += (_, e) => session.MaskEditing.CommitSegmentInsertion(e);
        canvas.MaskEditingExited += (_, _) => session.MaskEditing.ExitEditing();
        canvas.LayerEdited += async (_, e) => await viewModel.CommitCanvasAsync(e);
        canvas.RenderingFailed += (_, e) => viewModel.ReportRenderingError(e.Error);
        canvas.RenderingRecovered += (_, _) => viewModel.ReportRenderingRecovery();
        viewModel.PropertyChanged += OnSceneChanged;
        session.SceneGestureCancellationRequested += OnSceneGestureCancelled;
        ApplyScene();
        positionSlider = this.FindControl<Slider>("PositionSlider")!;
        synchronizedPosition = viewModel.Position;
        positionSlider.AddHandler(PointerPressedEvent, (_, e) =>
        {
            viewModel.IsScrubbing = positionSlider.IsEnabled && e.GetCurrentPoint(positionSlider).Properties.IsLeftButtonPressed;
        }, RoutingStrategies.Tunnel);
        positionSlider.AddHandler(PointerReleasedEvent, (_, _) =>
        {
            if (viewModel.IsScrubbing)
            {
                var target = MediaTime.FromTimeSpan(TimeSpan.FromSeconds(positionSlider.Value));
                _ = viewModel.SeekAsync(target);
                viewModel.IsScrubbing = false;
            }
        }, RoutingStrategies.Tunnel);
        positionSlider.PointerCaptureLost += (_, _) =>
        {
            if (viewModel.IsScrubbing)
            {
                session.CancelInteractiveSeeking();
                viewModel.IsScrubbing = false;
            }
        };
        positionSlider.ValueChanged += async (_, e) =>
        {
            if (Math.Abs(viewModel.Position - synchronizedPosition) > 0.000001)
            {
                synchronizedPosition = viewModel.Position;
                return;
            }
            if (positionSlider.IsEnabled && (viewModel.IsScrubbing || Math.Abs(e.NewValue - viewModel.Position) > 0.000001))
            {
                await viewModel.SeekAsync(MediaTime.FromTimeSpan(TimeSpan.FromSeconds(e.NewValue)));
            }
        };
        session.PreviewUpdated += OnPreviewUpdated;
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
    }

    public string PanelId => "preview";
    public void CancelGestures()
    {
        session.CancelInteractiveSeeking();
        viewModel.IsScrubbing = false;
        canvas.CancelGesture();
        viewModel.CancelCanvasGesture();
    }
    public void FocusInvalidField(string? fieldKey) => positionSlider.Focus();
    private void OnPreviewUpdated(object? sender, VideoPreviewUpdate update)
    {
        if (update.ClearFrame)
        {
            canvas.ClearVideo();
        }
        if (update.Frame is { } frame)
        {
            ApplyScene();
            var origin = update.CompositionDocument?.Media?.MediaOrigin ?? viewModel.Scene.Document.Media?.MediaOrigin ?? MediaTime.Zero;
            var start = update.Snapshot.PresentedFrameTime is { } time ? time - origin : (MediaTime?)null;
            var end = update.Snapshot.PresentedFrameEnd is { } next ? next - origin : (MediaTime?)null;
            canvas.PresentComposite(frame, update.BackgroundFrame ?? frame, update.CompositionTime ?? start,
                update.CompositionDocument, update.IsInteractiveComposition, start, end);
        }
        canvas.PlaybackActive = update.Snapshot.State == AegiNext.Media.Playback.VideoPlaybackState.PLAYING;
    }
    private void OnSceneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewPanelViewModel.Scene))
        {
            ApplyScene();
        }
        else if (e.PropertyName is nameof(PreviewPanelViewModel.IsPlaying) or nameof(PreviewPanelViewModel.IsMuted))
        {
            ApplyTransportIcons();
        }
    }
    private void ApplyTransportIcons()
    {
        this.FindControl<Button>("PlayButton")!.Content = WorkbenchIcon.Create(viewModel.IsPlaying ? "Pause" : "Play");
        this.FindControl<Button>("MuteButton")!.Content = WorkbenchIcon.Create(viewModel.IsMuted ? "Mute" : "Volume");
    }
    private void ApplyScene()
    {
        var scene = viewModel.Scene;
        var quality = PreviewQualityOptions.Get(scene.Quality);
        canvas.MaximumPreviewSize = new PixelSize(quality.MaximumWidth, quality.MaximumHeight);
        canvas.InteractivePreview = scene.IsInteractive;
        canvas.PlaybackActive = viewModel.IsPlaying;
        canvas.EditMode = scene.Mode;
        canvas.MaskSelectedNodeId = session.SceneEditing.MaskNodeId;
        canvas.SetScene(scene.Document, scene.SelectedLayer, scene.Position, scene.AssetDirectory, scene.IsEditingPose);
    }
    private void OnSceneGestureCancelled(object? sender, EventArgs e)
    {
        canvas.CancelGesture();
        viewModel.CancelCanvasGesture();
    }
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            session.PreviewUpdated -= OnPreviewUpdated;
            session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
            viewModel.PropertyChanged -= OnSceneChanged;
            session.SceneGestureCancellationRequested -= OnSceneGestureCancelled;
            canvas.Dispose();
        }
    }
}
