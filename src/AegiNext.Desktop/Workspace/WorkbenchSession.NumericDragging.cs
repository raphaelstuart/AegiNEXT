namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal void CancelNumericGestures() => NumericGestureCancellationRequested?.Invoke(this, EventArgs.Empty);
}
