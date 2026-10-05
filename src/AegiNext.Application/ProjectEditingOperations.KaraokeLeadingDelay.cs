using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>平移全部逐字时间以设置句前留白，保留每字时长、间隙、身份、样式及字幕图层边界。</summary>
    public static ProjectDocument SetKaraokeLeadingDelay(ProjectDocument document, Guid subtitleId,
        MediaTime delay, MediaTime animationOffset)
    {
        ProjectValidator.Validate(document);
        if (delay < MediaTime.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "句前留白不能为负。");
        }
        var index = SubtitleIndex(document, subtitleId);
        var line = document.Subtitles[index];
        if (line.Karaoke.IsEmpty)
        {
            return document;
        }
        var change = animationOffset + delay - line.Karaoke[0].Start;
        if (change == MediaTime.Zero)
        {
            return document;
        }
        var clips = line.Karaoke.Select(clip => clip with
        {
            Start = clip.Start + change,
            End = clip.End + change
        }).ToImmutableArray();
        if (clips.Any(clip => clip.Start < animationOffset || clip.Start < MediaTime.Zero))
        {
            throw new ArgumentOutOfRangeException(nameof(delay), "字时间不能早于字幕当前可见起点。");
        }
        return Verified(document with { Subtitles = document.Subtitles.SetItem(index, line with { Karaoke = clips }) });
    }
}
