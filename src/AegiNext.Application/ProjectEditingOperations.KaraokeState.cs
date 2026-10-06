using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>切换逐字高亮，关闭时保存完整片段，启用时优先恢复保存的数据；首次启用才按字素生成。</summary>
    public static ProjectDocument SetSubtitleKaraokeEnabled(ProjectDocument document, Guid subtitleId, bool enabled)
    {
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        if (!enabled)
        {
            return line.Karaoke.IsEmpty ? document : WithSubtitleContent(document, index, line with
            {
                Karaoke = [],
                InactiveKaraoke = line.Karaoke.AddRange(line.InactiveKaraoke).OrderBy(clip => clip.Utf16Start).ToImmutableArray()
            });
        }
        if (!line.InactiveKaraoke.IsEmpty)
        {
            return WithSubtitleContent(document, index, line with
            {
                Karaoke = line.Karaoke.AddRange(line.InactiveKaraoke).OrderBy(clip => clip.Utf16Start).ToImmutableArray(),
                InactiveKaraoke = []
            });
        }
        if (!line.Karaoke.IsEmpty || line.Text.Length == 0)
        {
            return document;
        }

        var boundaries = StringInfo.ParseCombiningCharacters(line.Text).Append(line.Text.Length).ToArray();
        var count = boundaries.Length - 1;
        var offset = SubtitleLayer(document.Layers, subtitleId).AnimationOffset;
        var duration = line.End - line.Start;
        var clips = ImmutableArray.CreateBuilder<KaraokeSegment>(count);
        for (var cursor = 0; cursor < count; cursor++)
        {
            var start = offset + duration * cursor / count;
            var end = offset + duration * (cursor + 1) / count;
            if (end <= MediaTime.Zero)
            {
                continue;
            }
            clips.Add(new(boundaries[cursor], boundaries[cursor + 1] - boundaries[cursor],
                start < MediaTime.Zero ? MediaTime.Zero : start, end, new(1, 0.6, 0)));
        }
        return WithSubtitleContent(document, index, line with { Karaoke = clips.ToImmutable() });
    }
}
