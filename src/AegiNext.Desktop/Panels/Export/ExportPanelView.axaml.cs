using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace AegiNext.Desktop.Panels.Export;

internal sealed partial class ExportPanelView : UserControl, IWorkbenchPanelView
{
    private readonly WorkbenchSession session;
    internal ExportPanelView(ExportPanelViewModel viewModel, WorkbenchSession session)
    {
        this.session = session;
        AvaloniaXamlLoader.Load(this);
        DataContext = viewModel;
        session.PreferencesChanged += OnPreferencesChanged;
        ControlLocalization.Apply(this);
    }
    public string PanelId => "export";
    public void CancelGestures()
    {
    }
    public void FocusInvalidField(string? fieldKey) => this.FindControl<Control>(fieldKey ?? "CodecCombo")?.Focus();
    private void OnPreferencesChanged(object? sender, EventArgs e) => ControlLocalization.Apply(this);
    public void Dispose() => session.PreferencesChanged -= OnPreferencesChanged;
}
