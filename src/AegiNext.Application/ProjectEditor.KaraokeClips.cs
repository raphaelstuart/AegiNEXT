using AegiNext.Core.Timing;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>以一次撤销事务独立修改计时组的起止时间。</summary>
    public void SetKaraokeClipRange(Guid subtitleId, Guid clipId, MediaTime start, MediaTime end)
    {
        Apply("Edit karaoke range", document => ProjectEditingOperations.SetKaraokeClipRange(document, subtitleId, clipId, start, end));
    }

    /// <summary>以一次撤销事务为明确选择的文字创建计时组。</summary>
    public void CreateKaraokeClip(Guid subtitleId, int utf16Start, int utf16Length, MediaTime start, MediaTime end)
    {
        Apply("Create karaoke timing", document => ProjectEditingOperations.CreateKaraokeClip(document,
            subtitleId, utf16Start, utf16Length, start, end));
    }
    /// <summary>以一次撤销事务在字素边界拆分卡拉 OK 片段。</summary>
    public void SplitKaraokeClip(Guid subtitleId, Guid clipId, int utf16Offset, MediaTime? splitTime = null)
    {
        Apply("Split karaoke clip", document => ProjectEditingOperations.SplitKaraokeClip(document, subtitleId, clipId, utf16Offset, splitTime));
    }

    /// <summary>以一次撤销事务将片段按字素均分。</summary>
    public void SplitKaraokeClipIntoGraphemes(Guid subtitleId, Guid clipId)
    {
        Apply("Split karaoke clip into graphemes", document => ProjectEditingOperations.SplitKaraokeClipIntoGraphemes(document, subtitleId, clipId));
    }

    /// <summary>以一次撤销事务合并紧邻且兼容的卡拉 OK 片段。</summary>
    public void MergeKaraokeClips(Guid subtitleId, Guid firstId, Guid secondId)
    {
        Apply("Merge karaoke clips", document => ProjectEditingOperations.MergeKaraokeClips(document, subtitleId, firstId, secondId));
    }
}
