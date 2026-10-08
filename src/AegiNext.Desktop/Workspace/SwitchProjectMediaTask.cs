using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Workspace;

internal sealed class SwitchProjectMediaTask(WorkbenchSession session, ProjectWorkflowCoordinator coordinator,
    string path, bool updateProject) : ProjectWorkflowTask<bool>(session)
{
    private readonly long inputRevision = session.TaskInputRevision;
    private readonly AegiNext.Core.Projects.ProjectDocument captured = session.Editor.Snapshot;
    public override string Name => "Tasks.SwitchMedia";
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. base.Resources, AegiTaskResource.DeferredStoragePath(path)];
    protected override async Task<bool> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (Session.TaskInputRevision != inputRevision || Session.HasProjectDrafts || !ReferenceEquals(captured, Session.Editor.Snapshot))
        {
            throw new OperationCanceledException("The project input changed before the operation started.", context.CancellationToken);
        }
        await coordinator.OpenMediaCoreAsync(path, updateProject, context);
        return true;
    }
}
