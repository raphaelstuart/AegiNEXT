using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Workspace;

internal abstract class ProjectWorkflowTask<TResult>(WorkbenchSession session) : AegiTask<TResult>
{
    protected WorkbenchSession Session { get; } = session;
    public override string ScopeId => Session.TaskScope;
    public override string ScopeDisplayName => Session.ProjectDisplayName;
    public override AegiTaskEditRestriction EditRestriction => AegiTaskEditRestriction.Scope;
    public override bool RestrictEditingDuringExecution => false;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
    [
        AegiTaskResource.Project(Session.TaskScope), AegiTaskResource.Named("media:" + Session.TaskScope),
        AegiTaskResource.DeferredStoragePath(Session.ProjectDirectory),
        AegiTaskResource.DeferredStoragePath(Path.Combine(Session.PreferencesStore.DirectoryPath, "recent-projects.json"))
    ];
}
