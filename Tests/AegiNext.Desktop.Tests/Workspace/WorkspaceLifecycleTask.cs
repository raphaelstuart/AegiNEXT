using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class WorkspaceLifecycleTask(string scopeId) : AegiTask
{
    internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Finish { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Cleanup { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override string Name => "Controlled workspace lifecycle";
    public override string ScopeId => scopeId;
    public override AegiTaskEditRestriction EditRestriction => AegiTaskEditRestriction.Scope;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [AegiTaskResource.Project(scopeId)];

    protected override async Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        Started.TrySetResult();
        try
        {
            await Finish.Task.WaitAsync(context.CancellationToken);
        }
        catch (OperationCanceledException)
        {
            CancellationObserved.TrySetResult();
            throw;
        }
        finally
        {
            await Cleanup.Task;
        }
    }
}
