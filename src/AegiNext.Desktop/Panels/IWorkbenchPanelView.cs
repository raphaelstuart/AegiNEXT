namespace AegiNext.Desktop.Panels;

internal interface IWorkbenchPanelView : IDisposable
{
    string PanelId { get; }
    void CancelGestures();
    void FocusInvalidField(string? fieldKey);
}
