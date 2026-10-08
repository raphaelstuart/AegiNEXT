namespace AegiNext.Application.Tasks;

internal sealed class AegiTaskScopeState(string displayName)
{
    internal string DisplayName { get; set; } = displayName;

    internal long Generation { get; set; }

    internal bool Accepting { get; set; } = true;

    internal bool Closing { get; set; }

    internal int EditLeases { get; set; }
}
