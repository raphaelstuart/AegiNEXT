using System.Collections.Immutable;
using System.Text.Json.Serialization;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Projects;

/// <summary>具有稳定标识与精确半开时间区间的字幕行；卡拉 OK 时间相对字幕层的内容原点。</summary>
public sealed record SubtitleLine
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public MediaTime Start { get; init; }
    public MediaTime End { get; init; } = new(2);
    public string Text { get; init; } = string.Empty;
    public string StyleName { get; init; } = "Default";
    public Guid? StylePresetId { get; init; }
    public Guid? ColorTagId { get; init; }
    public SubtitleStyle Style { get; init; } = new();
    public ImmutableArray<SubtitleInlineSpan> InlineSpans { get; init; } = [];
    public ImmutableArray<SubtitleAnimationRange> AnimationRanges { get; init; } = [];
    public ImmutableArray<SubtitleKaraokeStyleSpan> KaraokeStyleSpans { get; init; } = [];
    public ImmutableArray<KaraokeSegment> Karaoke { get; init; } = [];
    public ImmutableArray<KaraokeSegment> InactiveKaraoke { get; init; } = [];
    public KaraokeHighlightStyle? KaraokeStyle { get; init; }

    [JsonIgnore]
    public SubtitleContentKind ContentKind => !Karaoke.IsDefaultOrEmpty ? SubtitleContentKind.KARAOKE :
        !InlineSpans.IsDefaultOrEmpty && InlineSpans.Any(span => span.Style.HasOverrides)
            ? SubtitleContentKind.RICH_TEXT : SubtitleContentKind.PLAIN;
}
