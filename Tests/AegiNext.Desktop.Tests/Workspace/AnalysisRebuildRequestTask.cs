using AegiNext.Application.Tasks;
using AegiNext.Desktop.Settings.AudioAnalysis;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class AnalysisRebuildRequestTask(WorkbenchSession session, AudioAnalysisPreferences preferences,
    TaskCompletionSource accepted, Task release) : AegiTask
{
    public override string Name => "Parent analysis rebuild test";
    public override string ScopeId => session.TaskScope;
    public override IReadOnlyCollection<AegiTaskResource> Resources => session.ApplicationContext.SettingsResources;

    protected override async Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        await session.ApplicationContext.RebuildAudioAnalysisAsync(preferences);
        accepted.TrySetResult();
        await release.WaitAsync(context.CancellationToken);
    }
}
