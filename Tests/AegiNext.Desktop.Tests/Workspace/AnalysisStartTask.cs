using AegiNext.Application.Tasks;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class AnalysisStartTask(string scopeId, AnalysisCoordinator coordinator, string path) : AegiTask
{
    public override string Name => "Open audio analysis from a workflow";
    public override string ScopeId => scopeId;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [AegiTaskResource.Project(scopeId)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        return coordinator.StartAsync(path);
    }
}
