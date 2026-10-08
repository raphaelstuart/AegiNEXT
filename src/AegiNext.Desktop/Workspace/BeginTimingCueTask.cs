using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Workspace;

internal sealed class BeginTimingCueTask(WorkbenchSession session, Guid trackId, MediaTime start,
    TimingEnterResult entered, ProjectDocument source, long generation, Guid? presetId,
    TimelineTimingPreview preview, long inputRevision) : AegiTask
{
    public override string Name => "Tasks.CreateSubtitleClips";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override AegiTaskEditRestriction EditRestriction => AegiTaskEditRestriction.Scope;
    public override bool RestrictEditingDuringExecution => false;
    public override IReadOnlyCollection<AegiTaskResource> Resources => CreateSubtitleClipsTask.GetResources(session);
    protected override Task ExecuteAsync(AegiTaskExecutionContext context) =>
        session.ExecutePendingTimingCreationAsync(trackId, start, entered, source, generation, presetId, preview, inputRevision, context);
}
