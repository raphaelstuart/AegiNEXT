using AegiNext.Application.Tasks;
using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Workspace;

internal sealed class SwitchPreviewDecodeModeTask(WorkbenchSession session, VideoDecodeMode mode) : AegiTask<bool>
{
    public override string Name => "Tasks.SwitchDecodeMode";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override AegiTaskEditRestriction EditRestriction => AegiTaskEditRestriction.Scope;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
    [
        AegiTaskResource.Project(session.TaskScope), AegiTaskResource.Named("media:" + session.TaskScope),
        AegiTaskResource.DeferredStoragePath(Path.Combine(session.PreferencesStore.DirectoryPath, "preferences.json"))
    ];
    protected override Task<bool> ExecuteResultAsync(AegiTaskExecutionContext context) =>
        session.SwitchPreviewDecodeModeCoreAsync(mode, context);
}
