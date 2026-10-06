using System.Collections.Immutable;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Controls;

/// <summary>一次完成的多片段公共时间平移。</summary>
public sealed class TimelineClipsMoveEventArgs(Guid primaryId, IEnumerable<Guid> layerIds, MediaTime offset) : EventArgs
{
    public Guid PrimaryId { get; } = primaryId;
    public ImmutableArray<Guid> LayerIds { get; } = [.. layerIds];
    public MediaTime Offset { get; } = offset;
}
