using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Core.Editing;

/// <summary>以精确内容时间编辑计时组，按正文位置双向联动且保留已有间隙和重叠。</summary>
public static class KaraokeTimingEditing
{
    /// <summary>设置目标起止时间；联动时前组随起点、后组随终点平移，非法结果原子拒绝，无变化保留原数组。</summary>
    public static ImmutableArray<KaraokeSegment> SetRange(ImmutableArray<KaraokeSegment> clips, Guid clipId,
        MediaTime start, MediaTime end, bool linked = false)
    {
        if (clips.IsDefault)
        {
            throw new ArgumentException("计时组数组必须已初始化。", nameof(clips));
        }
        ValidateRange(start, end);
        var targetIndex = -1;
        for (var index = 0; index < clips.Length; index++)
        {
            if (clips[index].Id == clipId)
            {
                targetIndex = index;
                break;
            }
        }
        if (targetIndex < 0)
        {
            throw new KeyNotFoundException("卡拉 OK 计时组不存在。");
        }
        var target = clips[targetIndex];
        if (target.Start == start && target.End == end)
        {
            return clips;
        }
        var edited = clips.ToBuilder();
        edited[targetIndex] = target with { Start = start, End = end };
        if (!linked)
        {
            return edited.ToImmutable();
        }
        var startChange = start - target.Start;
        var endChange = end - target.End;
        for (var index = 0; index < clips.Length; index++)
        {
            if (index == targetIndex)
            {
                continue;
            }
            var clip = clips[index];
            var change = clip.Utf16Start < target.Utf16Start ? startChange : endChange;
            if (change == MediaTime.Zero)
            {
                continue;
            }
            var shiftedStart = clip.Start + change;
            var shiftedEnd = clip.End + change;
            ValidateRange(shiftedStart, shiftedEnd);
            edited[index] = clip with { Start = shiftedStart, End = shiftedEnd };
        }
        return edited.ToImmutable();
    }

    private static void ValidateRange(MediaTime start, MediaTime end)
    {
        if (start < MediaTime.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(start), "计时组不能早于内容时间原点。");
        }
        if (end <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(end), "计时组必须具有正时长。");
        }
    }
}
