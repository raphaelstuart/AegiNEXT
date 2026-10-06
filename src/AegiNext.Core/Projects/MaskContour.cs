using System.Collections.Immutable;

namespace AegiNext.Core.Projects;

/// <summary>具有稳定标识及有序节点的闭合蒙版轮廓，末节点连接至首节点。</summary>
public sealed record MaskContour
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public ImmutableArray<MaskNode> Nodes { get; init; } = [];
}
