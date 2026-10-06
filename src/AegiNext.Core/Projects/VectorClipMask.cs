using System.Collections.Immutable;

namespace AegiNext.Core.Projects;

/// <summary>按轮廓方向以非零环绕规则填充的闭合贝塞尔裁切。</summary>
public sealed record VectorClipMask : ClipMask
{
    public ImmutableArray<MaskContour> Contours { get; init; } = [];
}
