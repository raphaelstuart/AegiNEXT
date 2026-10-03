using Dock.Controls.DeferredContentControl;
using Dock.Model.Mvvm.Controls;

namespace AegiNext.Desktop.Layouts;

internal sealed class WorkbenchToolDock : ToolDock, IDeferredContentPresentation
{
    public bool DeferContentPresentation => false;
}
