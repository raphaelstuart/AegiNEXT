using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application.ColorTags;

/// <summary>个人常用颜色标记的版本化库文档；工程保留独立定义快照。</summary>
public sealed record SubtitleColorTagLibraryDocument
{
    public int Version { get; init; } = 1;
    public ImmutableArray<SubtitleColorTag> Tags { get; init; } = [];
}
