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
    private readonly VideoFramePresenter presenter;
    private readonly Slider positionSlider;
    private bool disposed;
    internal PreviewPanelView(PreviewPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        presenter = this.FindControl<VideoFramePresenter>("VideoImage")!;
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
    public void CancelGestures() => session.ViewModel.Preview.IsScrubbing = false;
    public void FocusInvalidField(string? fieldKey) => positionSlider.Focus();
    private void OnPreviewUpdated(object? sender, VideoPreviewUpdate update)
    {
        if (update.ClearFrame)
        {
            presenter.Clear();
        }
        if (update.Frame is { } frame)
        {
            presenter.Present(frame);
        }
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
            presenter.Dispose();
        }
    }
}
