using System.Collections.Immutable;

namespace AegiNext.Core.Projects;

/// <summary>随工程保存的时间轴视图状态，不参与字幕或特效内容的撤销历史。</summary>
public sealed record TimelineViewState
{
    private const int MAXIMUM_COLLAPSED_ROWS = 100000;

    public ImmutableArray<TimelineAnimationRowId> CollapsedAnimationRows { get; init; } = [];

    /// <summary>验证行身份与集合预算；暂时不存在的所属实体或属性允许保留。</summary>
    public void Validate()
    {
        if (CollapsedAnimationRows.IsDefault || CollapsedAnimationRows.Length > MAXIMUM_COLLAPSED_ROWS)
        {
            throw new InvalidDataException("时间轴折叠行集合无效或过大。");
        }

        var identities = new HashSet<TimelineAnimationRowId>();
        foreach (var row in CollapsedAnimationRows)
        {
            if (row is null || row.OwnerId == Guid.Empty || !Enum.IsDefined(row.Scope) ||
                !AnimationPropertyMetadata.CurrentProperties.Contains(row.Property) || !identities.Add(row))
            {
                throw new InvalidDataException("时间轴折叠行身份无效或重复。");
            }
        }
    }
}
