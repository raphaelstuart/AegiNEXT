using AegiNext.Application.Tasks;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Workspace;

internal sealed class AudioAnalysisBatchTask(WorkbenchSession session, AnalysisCoordinator coordinator,
    AudioAnalysisSession analysis, long epoch) : AegiTask
{
    internal long Epoch { get; } = epoch;
    public override string Name => "Tasks.AudioAnalysis";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
    [AegiTaskResource.Named("analysis:" + session.TaskScope), AegiTaskResource.DeferredStoragePath(analysis.CacheDirectory)];
    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        return coordinator.ExecuteBatchAsync(analysis, Epoch, context);
    }
}
