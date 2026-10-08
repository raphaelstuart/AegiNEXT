using System.Collections.Immutable;
using AegiNext.Application.Tasks;
using AegiNext.Desktop.Settings.Media;

namespace AegiNext.Desktop.Workspace;

internal sealed class ApplyAudioCalibrationTask(WorkbenchSession session, ImmutableArray<AudioDeviceCalibration> profiles) : AegiTask
{
    public override string Name => "Tasks.ApplyAudioCalibration";
    public override bool CanCancel => false;
    public override string ScopeId => session.TaskScope;
    public override string ScopeDisplayName => session.ProjectDisplayName;
    public override string CoalescingKey => "audio-calibration:" + session.TaskScope;
    public override AegiTaskEditRestriction EditRestriction => AegiTaskEditRestriction.Scope;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [AegiTaskResource.Project(session.TaskScope), AegiTaskResource.Named("media:" + session.TaskScope)];
    protected override Task ExecuteAsync(AegiTaskExecutionContext context) =>
        session.ApplyAudioCalibrationPreferencesCoreAsync(profiles, context);
}
