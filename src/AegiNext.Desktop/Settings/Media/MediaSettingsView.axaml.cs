using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using AegiNext.Desktop.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.Media;

/// <summary>媒体设置页，只呈现偏好并发出语义切换请求。</summary>
public sealed partial class MediaSettingsView : UserControl
{
    /// <summary>加载绑定前隔离宿主页面上下文。</summary>
    public MediaSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        var input = this.FindControl<NumericDraftInput>("AudioExtraDelayInput")!;
        var title = this.FindControl<NumericDragLabel>("AudioExtraDelayInputTitle")!;
        var focusRevision = 0L;
        MediaSettingsViewModel? dragModel = null;
        title.DragStarted += (_, _) =>
        {
            focusRevision++;
            dragModel = DataContext as MediaSettingsViewModel;
        };
        title.DragCompleted += (_, args) =>
        {
            focusRevision++;
            var frozenModel = dragModel;
            dragModel = null;
            if (args.Changed && IsEffectivelyVisible && ReferenceEquals(DataContext, frozenModel))
            {
                frozenModel?.CommitAudioCalibration();
            }
        };
        title.DragCanceled += (_, _) =>
        {
            focusRevision++;
            dragModel = null;
        };
        DataContextChanged += (_, _) =>
        {
            focusRevision++;
            title.CancelDrag();
        };
        PropertyChanged += (_, args) =>
        {
            if (args.Property == IsVisibleProperty && !IsVisible)
            {
                title.CancelDrag();
            }
        };
        input.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (DataContext is MediaSettingsViewModel model && args.Key is Key.Enter or Key.Escape && !HasComposition(input))
            {
                focusRevision++;
                if (args.Key == Key.Escape)
                {
                    model.RestoreAudioCalibration();
                }
                else
                {
                    model.CommitAudioCalibration();
                }
                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        input.LostFocus += (_, _) =>
        {
            var revision = ++focusRevision;
            var currentModel = DataContext as MediaSettingsViewModel;
            Dispatcher.UIThread.Post(() =>
            {
                if (revision == focusRevision && !input.IsTitleDragging && !input.IsKeyboardFocusWithin && IsEffectivelyVisible &&
                    currentModel is not null && ReferenceEquals(DataContext, currentModel) && !HasComposition(input))
                {
                    currentModel.CommitAudioCalibration();
                }
            }, DispatcherPriority.Background);
        };
    }

    private static bool HasComposition(Control input) => input.GetVisualDescendants().OfType<TextPresenter>()
        .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
}
