using AegiNext.Application.Tasks;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Tasks;
using Avalonia.Controls;

namespace AegiNext.Desktop.Views;

public sealed partial class MainWindow
{
    private TaskCenterViewModel? taskCenter;
    private TaskCenterView? taskCenterView;

    private void InitializeTaskCenter(AegiTaskService service)
    {
        taskCenter = new(service);
        taskCenterView = new() { DataContext = taskCenter };
        this.FindControl<WindowTitleBar>("TitleBar")!.RightContent = taskCenterView;
    }

    private void DisposeTaskCenter()
    {
        taskCenterView?.CloseFlyout();
        this.FindControl<WindowTitleBar>("TitleBar")!.RightContent = null;
        if (taskCenterView is { } view)
        {
            view.DataContext = null;
        }

        taskCenter?.Dispose();
        taskCenter = null;
        taskCenterView = null;
    }
}
