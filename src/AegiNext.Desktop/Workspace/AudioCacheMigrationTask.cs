using AegiNext.Application.Tasks;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Workspace;

internal sealed class AudioCacheMigrationTask(WorkbenchSession session, AnalysisCoordinator coordinator,
    AudioAnalysisSession analysis, long epoch, long revision, string destination, CancellationToken token) : AegiTask
{
    internal long Revision { get; } = revision;
    public override string Name => "Tasks.AudioCacheMigration";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
    [AegiTaskResource.Named("analysis:" + session.TaskScope), AegiTaskResource.StoragePath(analysis.CacheDirectory),
        AegiTaskResource.StoragePath(destination)];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        return coordinator.ExecuteMigrationAsync(analysis, epoch, Revision, destination, context, token);
    }
}
