using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Startup;

internal sealed class RecentProjectsWriteTask(RecentProjectService owner, IReadOnlyList<RecentProjectEntry> snapshot) : AegiTask
{
    public override string Name => "Tasks.RecentProjectsSave";
    public override bool CanCancel => false;
    public override string CoalescingKey => owner.Resource.Key;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [owner.Resource];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        context.EnterCommit();
        return owner.PersistAsync(snapshot);
    }
}
