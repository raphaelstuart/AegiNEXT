using AegiNext.Application.Tasks;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Workspace;

internal sealed class ImportStylePresetsTask(WorkbenchSession session, string path) : AegiTask
{
    public override string Name => "Tasks.ImportStyles";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [session.ApplicationContext.GetLibraryResource(PersonalLibraryKind.STYLE), AegiTaskResource.DeferredStoragePath(path)];
    protected override Task ExecuteAsync(AegiTaskExecutionContext context) => session.ApplicationContext.RunStyleOperationAsync(
        () => Task.Run(() => session.StyleLibrary.ImportAsync([path], () => context.EnterCommit(),
            context.CancellationToken), context.CancellationToken));
}
