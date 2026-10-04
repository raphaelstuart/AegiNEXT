using System.ComponentModel;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Workspace;
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
    private bool disposed;
    internal PreviewPanelView(PreviewPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        this.viewModel = viewModel;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        canvas = this.FindControl<EffectCanvasControl>("EffectCanvas")!;
        canvas.GestureStarting += (_, e) => e.Cancel = !viewModel.BeginCanvasGesture();
        canvas.GestureCancelled += (_, _) => viewModel.CancelCanvasGesture();
        canvas.LayerEdited += async (_, e) => await viewModel.CommitCanvasAsync(e);
        canvas.RenderingFailed += (_, e) => viewModel.ReportRenderingError(e.Error);
        canvas.RenderingRecovered += (_, _) => viewModel.ReportRenderingRecovery();
        viewModel.PropertyChanged += OnSceneChanged;
        session.SceneGestureCancellationRequested += OnGesturesCancelled;
        ApplyScene();
        positionSlider = this.FindControl<Slider>("PositionSlider")!;
        positionSlider.AddHandler(PointerPressedEvent, (_, e) =>
        {
            viewModel.IsScrubbing = positionSlider.IsEnabled && e.GetCurrentPoint(positionSlider).Properties.IsLeftButtonPressed;
        }, RoutingStrategies.Tunnel);
        positionSlider.AddHandler(PointerReleasedEvent, async (_, _) =>
        {
            if (viewModel.IsScrubbing)
            {
                var target = MediaTime.FromTimeSpan(TimeSpan.FromSeconds(positionSlider.Value));
                viewModel.IsScrubbing = false;
                await viewModel.SeekAsync(target);
            }
        }, RoutingStrategies.Tunnel);
        positionSlider.PointerCaptureLost += (_, _) => viewModel.IsScrubbing = false;
        positionSlider.ValueChanged += async (_, e) =>
        {
            if (!viewModel.IsScrubbing && positionSlider.IsEnabled && Math.Abs(e.NewValue - viewModel.Position) > 0.000001)
            {
                await viewModel.SeekAsync(MediaTime.FromTimeSpan(TimeSpan.FromSeconds(e.NewValue)));
            }
        };
        session.PreviewUpdated += OnPreviewUpdated;
        session.PreferencesChanged += OnPreferencesChanged;
        session.ViewModel.GesturesCancelled += OnGesturesCancelled;
        ControlLocalization.Apply(this);
    }

    public string PanelId => "preview";
    public void CancelGestures()
    {
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
            canvas.PresentComposite(frame, update.BackgroundFrame ?? frame);
            ApplyScene();
        }
    }
    private void OnSceneChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PreviewPanelViewModel.Scene))
        {
            ApplyScene();
        }
    }
    private void ApplyScene()
    {
        var scene = viewModel.Scene;
        canvas.EditMode = scene.Mode;
        canvas.SetScene(scene.Document, scene.SelectedLayer, scene.Position, scene.AssetDirectory, scene.IsEditingPose);
    }
    private void OnPreferencesChanged(object? sender, EventArgs e) => ControlLocalization.Apply(this);
    private void OnGesturesCancelled(object? sender, EventArgs e) => CancelGestures();
    public void Dispose()
    {
        if (!disposed)
        {
            disposed = true;
            session.PreviewUpdated -= OnPreviewUpdated;
            session.PreferencesChanged -= OnPreferencesChanged;
            session.ViewModel.GesturesCancelled -= OnGesturesCancelled;
            viewModel.PropertyChanged -= OnSceneChanged;
            session.SceneGestureCancellationRequested -= OnGesturesCancelled;
            canvas.Dispose();
        }
    }
}
