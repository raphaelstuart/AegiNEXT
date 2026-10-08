using AegiNext.Desktop.Controls;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
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
        input.AddHandler(KeyDownEvent, (_, args) =>
        {
            if (DataContext is TaskSettingsViewModel model && args.Key is Key.Enter or Key.Escape &&
                !HasComposition(input))
            {
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
            if (IsEffectivelyVisible && DataContext is TaskSettingsViewModel model && !HasComposition(input))
            {
                model.CommitMaximumConcurrentTasks();
            }
        };
    }

    private static bool HasComposition(Control input) => input.GetVisualDescendants().OfType<TextPresenter>()
        .Any(presenter => !string.IsNullOrEmpty(presenter.PreeditText));
}
