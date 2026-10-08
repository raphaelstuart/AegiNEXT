using AegiNext.Application.Tasks;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Workspace;

internal sealed class ImportEffectScriptsTask(WorkbenchSession session, string path) : AegiTask
{
    public override string Name => "Tasks.ImportEffectScripts";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [session.ApplicationContext.GetLibraryResource(PersonalLibraryKind.EFFECT), AegiTaskResource.DeferredStoragePath(path)];
    protected override Task ExecuteAsync(AegiTaskExecutionContext context) => session.ApplicationContext.RunEffectOperationAsync(
        () => Task.Run(() => session.EffectScriptLibrary.ImportAsync([path], () => context.EnterCommit(),
            context.CancellationToken), context.CancellationToken));
}
