using AegiNext.Application;
using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Workspace;

internal sealed class CreateProjectTask(WorkbenchSession session, ProjectWorkflowCoordinator coordinator,
    string path, ProjectCreationRequest? request) : ProjectWorkflowTask<ProjectOpenResult>(session)
{
    private readonly long inputRevision = session.TaskInputRevision;
    private readonly AegiNext.Core.Projects.ProjectDocument captured = session.Editor.Snapshot;
    public override string Name => "Tasks.CreateProject";
    internal ProjectOpenResult? Outcome { get; private set; }
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. base.Resources, AegiTaskResource.DeferredStoragePath(path), AegiTaskResource.DeferredStoragePath(Path.GetDirectoryName(Path.GetFullPath(path))!)];
    protected override async Task<ProjectOpenResult> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (Session.TaskInputRevision != inputRevision || Session.HasProjectDrafts || !ReferenceEquals(captured, Session.Editor.Snapshot))
        {
            throw new OperationCanceledException("The project input changed before the operation started.", context.CancellationToken);
        }
        var result = await coordinator.CreateProjectCoreAsync(path, request, context);
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
