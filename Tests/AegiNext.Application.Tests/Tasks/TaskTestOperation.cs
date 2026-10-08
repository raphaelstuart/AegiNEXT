using AegiNext.Application.Tasks;

namespace AegiNext.Application.Tests.Tasks;

internal sealed class TaskTestOperation : AegiTask
{
    internal Func<AegiTaskExecutionContext, Task> Execute { get; init; } = _ => Task.CompletedTask;

    public override string Name { get; } = "test";

    public override AegiTaskMode Mode { get; }

    public override bool CanCancel { get; } = true;

    public override AegiTaskEditRestriction EditRestriction { get; }

    public override bool RestrictEditingDuringExecution => RestrictEntireExecution ?? base.RestrictEditingDuringExecution;

    internal bool? RestrictEntireExecution { get; init; }

    public override string? ScopeId { get; }

    public override IReadOnlyCollection<AegiTaskResource> Resources { get; } = Array.Empty<AegiTaskResource>();

    public override string? CoalescingKey { get; }

    internal TaskTestOperation(string name = "test", string? scopeId = null,
        AegiTaskMode mode = AegiTaskMode.Parallel, bool canCancel = true,
        AegiTaskEditRestriction restriction = AegiTaskEditRestriction.None,
        IReadOnlyCollection<AegiTaskResource>? resources = null, string? coalescingKey = null)
    {
        Name = name;
        ScopeId = scopeId;
        Mode = mode;
        CanCancel = canCancel;
        EditRestriction = restriction;
        Resources = resources ?? Array.Empty<AegiTaskResource>();
        CoalescingKey = coalescingKey;
    }

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        return Execute(context);
    }
}
