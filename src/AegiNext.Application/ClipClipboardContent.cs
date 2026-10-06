using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

/// <summary>同工程剪贴板的不可变片段快照，固定源轨道顺序和复制时的轨道基准，资源保留工程身份。</summary>
public sealed record ClipClipboardContent(
    Guid SourceProjectId,
    Guid PrimaryId,
    MediaTime EarliestStart,
    ImmutableArray<ProjectLayer> Layers,
    ImmutableArray<SubtitleLine> Subtitles,
    ImmutableArray<Guid> SourceTrackIds = default,
    Guid? ReferenceTrackId = null);
