using AegiNext.Application.Tasks;
using AegiNext.Core.Projects;
using AegiNext.Core.Presets;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Workspace;

internal sealed class TimingPostProcessingTask(WorkbenchSession session, ProjectDocument source, long inputRevision,
    TimingPostProcessorOptions? options, IReadOnlySet<string>? styleNames, IReadOnlySet<Guid>? selected,
    IReadOnlyDictionary<Guid, SubtitleStylePreset>? associations) : AegiTask<int>
{
    public override string Name => "Tasks.TimingPostProcessor";
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override AegiTaskEditRestriction EditRestriction => AegiTaskEditRestriction.Scope;
    public override bool RestrictEditingDuringExecution => false;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
    [AegiTaskResource.Project(session.TaskScope), AegiTaskResource.Named("media:" + session.TaskScope)];

    protected override Task<int> ExecuteResultAsync(AegiTaskExecutionContext context) =>
        session.ExecuteTimingProcessingAsync(source, inputRevision, options, styleNames, selected, associations, context);
}
