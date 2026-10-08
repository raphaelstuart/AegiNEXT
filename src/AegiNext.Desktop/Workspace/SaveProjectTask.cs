using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed class SaveProjectTask(WorkbenchSession session, ProjectWorkflowCoordinator coordinator,
    string destination, ProjectDocument captured, ProjectDocument persistenceSnapshot, string sourceDirectory, bool finalization = false)
    : ProjectWorkflowTask<bool>(session)
{
    public override string Name => "Tasks.SaveProject";
    public override bool CanCancel => !finalization;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
    [.. base.Resources, AegiTaskResource.DeferredStoragePath(destination),
        AegiTaskResource.DeferredStoragePath(Path.GetDirectoryName(Path.GetFullPath(destination))!)];
    protected override Task<bool> ExecuteResultAsync(AegiTaskExecutionContext context) =>
        coordinator.SaveProjectCoreAsync(destination, captured, persistenceSnapshot, sourceDirectory, context, finalization);
}
