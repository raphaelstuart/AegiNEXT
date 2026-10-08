using AegiNext.Application;
using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed class MergeProjectsTask(WorkbenchSession session, ProjectWorkflowCoordinator coordinator,
    ProjectDocument captured, string directory, IReadOnlyList<string> paths, long inputRevision)
    : ProjectWorkflowTask<ProjectMergeResult>(session)
{
    public override string Name => "Tasks.MergeProjects";
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. base.Resources, .. paths.Select(AegiTaskResource.DeferredStoragePath)];
    protected override Task<ProjectMergeResult> ExecuteResultAsync(AegiTaskExecutionContext context) =>
        coordinator.MergeProjectsCoreAsync(captured, directory, paths, inputRevision, context);
}
