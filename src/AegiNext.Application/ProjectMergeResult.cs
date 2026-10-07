using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>完整合并快照及按来源顺序导入的身份；图层身份包含按先序遍历排列的所有子节点。</summary>
public sealed record ProjectMergeResult(
    ProjectDocument Document,
    ImmutableArray<Guid> ImportedTrackIds,
    ImmutableArray<Guid> ImportedLayerIds,
    ImmutableArray<Guid> ImportedSubtitleIds);
