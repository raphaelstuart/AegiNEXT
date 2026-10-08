using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Workspace;

internal sealed class RebuildAudioOutputTask(WorkbenchSession session) : AegiTask
{
    public override string Name => "Tasks.RebuildAudioOutput";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override AegiTaskEditRestriction EditRestriction => AegiTaskEditRestriction.Scope;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [AegiTaskResource.Project(session.TaskScope), AegiTaskResource.Named("media:" + session.TaskScope)];
    protected override Task ExecuteAsync(AegiTaskExecutionContext context) => session.RebuildAudioDeviceCoreAsync(context);
}
