using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Workspace;

internal sealed class AutomaticProjectPersistenceTask(ProjectPersistenceCoordinator owner, string scopeId,
    ProjectPersistenceState state, bool autoSave, long version, long revision) : AegiTask
{
    public override string Name => autoSave ? "Tasks.ProjectAutoSave" : "Tasks.ProjectBackup";
    public override string ScopeId => scopeId;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
    [AegiTaskResource.Project(scopeId), AegiTaskResource.StoragePath(state.ProjectPath)];
    public override string CoalescingKey => $"{Name}:{version}:{revision}:{Resources.Last().Key}";

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        return owner.RunTickAsync(state, autoSave, version, revision);
    }
}
