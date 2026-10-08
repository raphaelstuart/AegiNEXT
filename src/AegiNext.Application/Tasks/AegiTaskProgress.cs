namespace AegiNext.Application.Tasks;

/// <summary>Describes a task stage and optionally a measured amount of completed work.</summary>
public sealed record AegiTaskProgress(string? Stage = null, double? Completed = null, double? Total = null)
{
    /// <summary>Gets a normalized fraction only when the total is known and positive.</summary>
    public double? Fraction => Total is > 0 && Completed is { } completed
        ? Math.Clamp(completed / Total.Value, 0, 1)
        : null;
}
