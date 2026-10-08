using AegiNext.Application.Tasks;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Workspace;

using AegiNext.Application.Presets;

internal sealed class PrepareSubtitleStyleTask(WorkbenchSession session,
    Guid? trackId, SubtitleStylePreset? preset, ProjectDocument project, string directory) : AegiTask<PreparedSubtitleStyle>
{
    public override string Name => "Tasks.PrepareSubtitleStyle";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
    [AegiTaskResource.Project(session.TaskScope), AegiTaskResource.DeferredStoragePath(directory),
        session.ApplicationContext.GetLibraryResource(PersonalLibraryKind.STYLE)];
    protected override Task<PreparedSubtitleStyle> ExecuteResultAsync(AegiTaskExecutionContext context) =>
        StyleLibraryCoordinator.PrepareCreationCoreAsync(trackId, preset, project, directory, context.CancellationToken);
}
