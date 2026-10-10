using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Settings.Tasks;

/// <summary>呈现任务设置，连接字段确认、恢复和输入法组合保护。</summary>
public sealed partial class TaskSettingsView : UserControl
{
    /// <summary>加载任务并行上限输入。</summary>
    public TaskSettingsView()
    {
        DataContext = null;
        AvaloniaXamlLoader.Load(this);
        var input = this.FindControl<NumericDraftInput>("MaximumConcurrentTasksInput")!;
        var title = this.FindControl<NumericDragLabel>("MaximumConcurrentTasksInputTitle")!;
        var focusRevision = 0L;
        TaskSettingsViewModel? dragModel = null;
        title.DragStarted += (_, _) =>
        {
            focusRevision++;
            dragModel = DataContext as TaskSettingsViewModel;
        };
        title.DragCompleted += (_, args) =>
        {
            focusRevision++;
            var frozenModel = dragModel;
            dragModel = null;
            if (args.Changed && IsEffectivelyVisible && ReferenceEquals(DataContext, frozenModel))
            {
                frozenModel?.CommitMaximumConcurrentTasks();
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
            if (DataContext is TaskSettingsViewModel model && args.Key is Key.Enter or Key.Escape &&
                !HasComposition(input))
            {
                focusRevision++;
                if (args.Key == Key.Escape)
                {
                    model.RestoreMaximumConcurrentTasks();
                }
                else
                {
                    model.CommitMaximumConcurrentTasks();
                }

                args.Handled = true;
            }
        }, RoutingStrategies.Tunnel);
        input.LostFocus += (_, _) =>
        {
            var revision = ++focusRevision;
            var currentModel = DataContext as TaskSettingsViewModel;
            Dispatcher.UIThread.Post(() =>
            {
                if (revision == focusRevision && !input.IsTitleDragging && !input.IsKeyboardFocusWithin && IsEffectivelyVisible &&
                    currentModel is not null && ReferenceEquals(DataContext, currentModel) && !HasComposition(input))
                {
                    currentModel.CommitMaximumConcurrentTasks();
                }
            }, DispatcherPriority.Background);
        };
    }

    private static bool HasComposition(Control input) => input.GetVisualDescendants().OfType<TextPresenter>()
        .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
}
