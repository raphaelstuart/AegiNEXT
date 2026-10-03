using Dock.Controls.DeferredContentControl;
using Dock.Model.Mvvm.Controls;

namespace AegiNext.Desktop.Layouts;

internal sealed class WorkbenchRootDock : RootDock, IDeferredContentPresentation
{
    public bool DeferContentPresentation => false;
}
