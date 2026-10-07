using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controllers;

internal sealed record ObservedVideoPreparationCost
{
    internal ObservedVideoPreparationCost(MediaTime cost, long timestamp)
    {
        Cost = Quantize(cost);
        Timestamp = timestamp;
    }

    public MediaTime Cost { get; }
    public long Timestamp { get; }

    internal static MediaTime Quantize(MediaTime cost)
    {
        return MediaTime.FromTimeSpan(cost.ToTimeSpan(MediaTimeRounding.CEILING));
    }
}
