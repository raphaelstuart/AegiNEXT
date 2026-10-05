using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Panels.Export;

internal sealed partial class ExportPanelView : UserControl, IWorkbenchPanelView
{
    internal ExportPanelView(ExportPanelViewModel viewModel, WorkbenchSession session)
    {
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
    }
    public string PanelId => "export";
    public void CancelGestures()
    {
    }
    public void FocusInvalidField(string? fieldKey) => this.FindControl<Control>(fieldKey ?? "CodecCombo")?.Focus();
    public void Dispose()
    {
    }
}
