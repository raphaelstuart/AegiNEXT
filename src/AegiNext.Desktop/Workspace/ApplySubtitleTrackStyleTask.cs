using AegiNext.Application.Tasks;
using AegiNext.Core.Presets;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Startup;

namespace AegiNext.Desktop.Workspace;

internal sealed class ApplySubtitleTrackStyleTask(WorkbenchSession session, StyleLibraryCoordinator coordinator,
    SubtitleStylePreset preset, Guid trackId, TrackStyleUpdateDecision decision, ProjectDocument captured,
    long inputRevision, string directory) : ProjectWorkflowTask<bool>(session)
{
    public override string Name => "Tasks.ApplyTrackStyle";
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [.. base.Resources, Session.ApplicationContext.GetLibraryResource(PersonalLibraryKind.STYLE)];
    protected override async Task<bool> ExecuteResultAsync(AegiTaskExecutionContext context)
    {
        await coordinator.ApplyTrackCoreAsync(preset, trackId, decision, captured, inputRevision, directory, context);
        return true;
    }
}
