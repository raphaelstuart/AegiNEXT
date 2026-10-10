using System.Collections.Immutable;

namespace AegiNext.Desktop.Controls;

/// <summary>卡拉 OK 轴上的稳定片段选中请求。</summary>
public sealed class KaraokeClipSelectionEventArgs(IReadOnlyList<Guid> selectedClipIds, Guid? primaryClipId) : EventArgs
{
    /// <summary>按文字顺序排列的选中片段稳定 ID 快照。</summary>
    public IReadOnlyList<Guid> SelectedClipIds { get; } = selectedClipIds.ToImmutableArray();
    /// <summary>当前用于单片段属性和拖拽的主选择；空选择时为 null。</summary>
    public Guid? PrimaryClipId { get; } = primaryClipId;
}
