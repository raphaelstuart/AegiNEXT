using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class QueuedSaveGateTask(string scopeId) : AegiTask
{
    internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public override string Name => "Save capture test gate";
    public override string ScopeId => scopeId;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [AegiTaskResource.Project(scopeId)];
    protected override async Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        Entered.TrySetResult();
        await Released.Task.WaitAsync(context.CancellationToken);
    }
}
