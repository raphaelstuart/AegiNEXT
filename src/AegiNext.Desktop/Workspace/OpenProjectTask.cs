using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Workspace;

internal sealed class OpenProjectTask(WorkbenchSession session, ProjectWorkflowCoordinator coordinator,
    string path) : ProjectWorkflowTask<ProjectOpenResult>(session)
{
    private readonly long inputRevision = session.TaskInputRevision;
    private readonly AegiNext.Core.Projects.ProjectDocument captured = session.Editor.Snapshot;
    public override string Name => "Tasks.OpenProject";
    internal ProjectOpenResult? Outcome { get; private set; }
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. base.Resources, AegiTaskResource.StoragePath(path)];
    protected override async Task<ProjectOpenResult> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (Session.TaskInputRevision != inputRevision || Session.HasProjectDrafts || !ReferenceEquals(captured, Session.Editor.Snapshot))
        {
            throw new OperationCanceledException("The project input changed before the operation started.", context.CancellationToken);
        }
        var result = await coordinator.OpenProjectCoreAsync(path, context);
        Outcome = result;
        if (result.Status == ProjectOpenStatus.FAILED)
        {
            throw result.Error ?? new InvalidOperationException("Project preparation failed.");
        }
        if (result.Status == ProjectOpenStatus.CANCELLED)
        {
            throw new OperationCanceledException(context.CancellationToken);
        }
        return result;
    }
}
