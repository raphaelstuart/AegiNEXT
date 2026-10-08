using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Workspace;

internal sealed class SynchronizeProjectMediaTask(WorkbenchSession session, ProjectWorkflowCoordinator coordinator)
    : ProjectWorkflowTask<bool>(session)
{
    public override string Name => "Tasks.SynchronizeMedia";
    protected override async Task<bool> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        await coordinator.SynchronizePreviewBindingCoreAsync(context);
        return true;
    }
}
