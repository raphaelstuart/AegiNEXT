using AegiNext.Application.Tasks;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Workspace;

internal sealed class RebuildAudioAnalysisTask(WorkbenchSession session, AnalysisCoordinator coordinator,
    AudioAnalysisSession analysis, long epoch) : AegiTask
{
    public override string Name => "Tasks.AudioAnalysisRebuild";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
    [AegiTaskResource.Named("analysis:" + session.TaskScope), AegiTaskResource.StoragePath(analysis.CacheDirectory)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        return coordinator.ExecuteBatchAsync(analysis, epoch, context);
    }
}
