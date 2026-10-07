namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal Task MergeProjectsAsync(IReadOnlyList<string> paths, CancellationToken cancellationToken = default)
    {
        return RunCommandAsync(() => workflow.MergeProjectsAsync(paths, cancellationToken));
    }
}
