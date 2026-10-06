using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>一次完整粘贴产生的工程快照、按合成顺序排列的片段身份与主选择身份。</summary>
public sealed record ClipPasteResult(ProjectDocument Document, ImmutableArray<Guid> LayerIds, Guid PrimaryId);
