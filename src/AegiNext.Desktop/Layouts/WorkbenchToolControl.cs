using Dock.Avalonia.Controls;
using Dock.Controls.DeferredContentControl;

namespace AegiNext.Desktop.Layouts;

internal sealed class WorkbenchToolControl : ToolControl, IDeferredContentPresentation
{
    public bool DeferContentPresentation => false;

    protected override Type StyleKeyOverride => typeof(ToolControl);
}
