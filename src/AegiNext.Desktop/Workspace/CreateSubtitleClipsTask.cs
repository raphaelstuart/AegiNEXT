using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Workspace;

internal sealed class CreateSubtitleClipsTask(WorkbenchSession session, IReadOnlyList<SubtitleLine> imported,
    Guid trackId, Guid? fallbackPresetId, ProjectDocument source, long inputRevision) : AegiTask
{
    public override string Name => "Tasks.CreateSubtitleClips";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override AegiTaskEditRestriction EditRestriction => AegiTaskEditRestriction.Scope;
    public override bool RestrictEditingDuringExecution => false;
    public override IReadOnlyCollection<AegiTaskResource> Resources => GetResources(session);
    internal static IReadOnlyCollection<AegiTaskResource> GetResources(WorkbenchSession owner) =>
    [AegiTaskResource.Project(owner.TaskScope), AegiTaskResource.DeferredStoragePath(owner.ProjectDirectory),
        owner.ApplicationContext.GetLibraryResource(PersonalLibraryKind.STYLE), AegiTaskResource.Named("media:" + owner.TaskScope)];
    protected override Task ExecuteAsync(AegiTaskExecutionContext context) =>
        session.CreateSubtitleClipsCoreAsync(imported, trackId, fallbackPresetId, source, inputRevision, context);
}
