using AegiNext.Application.Tasks;

namespace AegiNext.Desktop.Layouts;

internal sealed class LayoutWriteTask(WorkspaceLayoutStore owner, byte[] bytes) : AegiTask
{
    public override string Name => "Tasks.LayoutSave";
    public override bool CanCancel => false;
    public override string CoalescingKey => owner.Resource.Key;
    public override IReadOnlyCollection<AegiTaskResource> Resources => [owner.Resource];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        context.EnterCommit();
        return owner.WriteAsync(bytes);
    }
}
