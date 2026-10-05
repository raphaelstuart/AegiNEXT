using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application.SubtitleFormats;

/// <summary>高级标签编辑的有效内容、诊断与文字来源映射。</summary>
public sealed record AssTextEditResult(SubtitleLine Line, ImmutableArray<SubtitleFormatDiagnostic> Diagnostics,
    ImmutableArray<AssSourceMapEntry> SourceMap)
{
    internal ImmutableArray<AssKaraokeSourceMapEntry> KaraokeSourceMap { get; init; } = [];
}
