using Avalonia.Controls;
using Dock.Controls.DeferredContentControl;
using Dock.Model.Mvvm.Controls;

namespace AegiNext.Desktop.Layouts;

internal sealed class WorkbenchDockPanel : Tool, IDeferredContentPresentation
{
    internal WorkbenchDockPanel(string panelId, Control view, string title)
    {
        Id = panelId;
        Title = title;
        View = view;
        CanPin = false;
        CanClose = true;
        CanFloat = true;
        CanDockAsDocument = false;
    }

    public Control View { get; }
    public bool DeferContentPresentation => false;
}
