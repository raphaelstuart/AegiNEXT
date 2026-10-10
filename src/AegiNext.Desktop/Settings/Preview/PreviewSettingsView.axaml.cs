using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.Preview;

/// <summary>呈现预览设置，并将确认与恢复操作交给页面草稿模型。</summary>
public sealed partial class PreviewSettingsView : UserControl
{
    /// <summary>加载试听长度输入并连接本地键盘和焦点操作。</summary>
    public PreviewSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        var input = this.FindControl<NumericDraftInput>("SubtitleAuditionMillisecondsInput")!;
        var title = this.FindControl<NumericDragLabel>("SubtitleAuditionMillisecondsInputTitle")!;
        var focusRevision = 0L;
        PreviewSettingsViewModel? dragModel = null;
        title.DragStarted += (_, _) =>
        {
            focusRevision++;
            dragModel = DataContext as PreviewSettingsViewModel;
        };
        title.DragCompleted += (_, args) =>
        {
            focusRevision++;
            var frozenModel = dragModel;
            dragModel = null;
            if (args.Changed && IsEffectivelyVisible && ReferenceEquals(DataContext, frozenModel))
            {
                frozenModel?.CommitSubtitleAuditionMilliseconds();
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
            if (DataContext is PreviewSettingsViewModel model && args.Key is Key.Enter or Key.Escape &&
                !HasComposition(input))
            {
                focusRevision++;
                if (args.Key == Key.Escape)
                {
                    model.RestoreSubtitleAuditionMilliseconds();
                }
                else
                {
                    model.CommitSubtitleAuditionMilliseconds();
                }

                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        input.LostFocus += (_, _) =>
        {
            var revision = ++focusRevision;
            var currentModel = DataContext as PreviewSettingsViewModel;
            Dispatcher.UIThread.Post(() =>
            {
                if (revision == focusRevision && !input.IsTitleDragging && !input.IsKeyboardFocusWithin && IsEffectivelyVisible &&
                    currentModel is not null && ReferenceEquals(DataContext, currentModel) && !HasComposition(input))
                {
                    currentModel.CommitSubtitleAuditionMilliseconds();
                }
            }, DispatcherPriority.Background);
        };
    }

    private static bool HasComposition(Control input) => input.GetVisualDescendants().OfType<TextPresenter>()
        .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
}
