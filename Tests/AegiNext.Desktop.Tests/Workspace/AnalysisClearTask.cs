using AegiNext.Application.Tasks;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class AnalysisClearTask(string scopeId, AnalysisCoordinator coordinator) : AegiTask
{
    public override string Name => "Clear yielded audio analysis";
    public override string ScopeId => scopeId;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [AegiTaskResource.Project(scopeId)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        return coordinator.ClearAsync();
    }
}
