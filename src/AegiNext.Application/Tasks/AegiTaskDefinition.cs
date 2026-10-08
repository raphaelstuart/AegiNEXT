namespace AegiNext.Application.Tasks;

internal sealed record AegiTaskDefinition(string Name, AegiTaskMode Mode, bool CanCancel,
    AegiTaskEditRestriction EditRestriction, bool RestrictEditingDuringExecution,
    string? ScopeId, string? ScopeDisplayName, HashSet<AegiTaskResource> Resources,
    string? CoalescingKey, Type TaskType, bool HasResult)
{
    internal static AegiTaskDefinition Capture(AegiTask task)
    {
        var definition = new AegiTaskDefinition(task.Name, task.Mode, task.CanCancel, task.EditRestriction,
            task.RestrictEditingDuringExecution, task.ScopeId, task.ScopeDisplayName, task.Resources.ToHashSet(),
            task.CoalescingKey, task.GetType(), task.HasResult);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Name);
        if (!Enum.IsDefined(definition.Mode) || !Enum.IsDefined(definition.EditRestriction))
        {
            throw new ArgumentException("Task execution policies must be supported values.", nameof(task));
        }

        if (definition.Resources.Any(resource => string.IsNullOrWhiteSpace(resource.Key)))
        {
            throw new ArgumentException("Task resources must have valid identities.", nameof(task));
        }

        if (definition.EditRestriction == AegiTaskEditRestriction.Scope && definition.ScopeId is null)
        {
            throw new ArgumentException("A project editing restriction requires an owning scope.", nameof(task));
        }

        return definition;
    }
}
