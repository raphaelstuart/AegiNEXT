namespace AegiNext.Core.Timing;

/// <summary>Specifies the lead, adjacency, and video keyframe timing stages associated with a subtitle style.</summary>
public sealed record TimingPostProcessorOptions
{
    public bool LeadInEnabled { get; init; } = true;
    public int LeadInMilliseconds { get; init; } = 100;
    public bool LeadOutEnabled { get; init; } = true;
    public int LeadOutMilliseconds { get; init; } = 350;
    public bool AdjacencyEnabled { get; init; } = true;
    public int MaximumGapMilliseconds { get; init; } = 300;
    public int MaximumOverlapMilliseconds { get; init; } = 50;
    public int BiasPercent { get; init; } = 90;
    public bool KeyframeSnapEnabled { get; init; } = true;
    public int StartBeforeMilliseconds { get; init; } = 200;
    public int StartAfterMilliseconds { get; init; } = 150;
    public int EndBeforeMilliseconds { get; init; } = 200;
    public int EndAfterMilliseconds { get; init; } = 250;

    /// <summary>Rejects negative timing windows and biases outside the inclusive 0–100 range.</summary>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegative(LeadInMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(LeadOutMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumGapMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(MaximumOverlapMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(StartBeforeMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(StartAfterMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(EndBeforeMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(EndAfterMilliseconds);
        ArgumentOutOfRangeException.ThrowIfNegative(BiasPercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(BiasPercent, 100);
    }
}
