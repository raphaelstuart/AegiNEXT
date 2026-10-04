using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>新增具有稳定身份的字幕轨道，作为一个可撤销事务。</summary>
    public Guid AddSubtitleTrack(string name)
    {
        var track = new SubtitleTrack { Name = name };
        Apply("Add subtitle track", document => ProjectEditingOperations.AddSubtitleTrack(document, track));
        return track.Id;
    }

    /// <summary>重命名字幕轨道，保留片段与效果身份。</summary>
    public void RenameSubtitleTrack(Guid trackId, string name)
    {
        Apply("Rename subtitle track", document => ProjectEditingOperations.RenameSubtitleTrack(document, trackId, name));
    }

    /// <summary>删除空字幕轨道；最后一条轨道及含片段轨道明确拒绝。</summary>
    public void RemoveSubtitleTrack(Guid trackId)
    {
        Apply("Remove subtitle track", document => ProjectEditingOperations.RemoveSubtitleTrack(document, trackId));
    }

    /// <summary>调整轨道显示顺序，不修改合成树顺序。</summary>
    public void MoveSubtitleTrack(Guid trackId, int newIndex)
    {
        Apply("Reorder subtitle tracks", document => ProjectEditingOperations.MoveSubtitleTrack(document, trackId, newIndex));
    }

    /// <summary>将字幕移入目标轨道，保留时间、图层与动画；碰撞拒绝整个事务。</summary>
    public void MoveSubtitleToTrack(Guid subtitleId, Guid trackId)
    {
        Apply("Move subtitle to track", document => ProjectEditingOperations.MoveSubtitleToTrack(document, subtitleId, trackId));
    }

    /// <summary>原子调整片段轨道和区间；整体移动保留内容相位，裁剪或拉伸使用既有动画语义。</summary>
    public void MoveSubtitleClip(Guid subtitleId, Guid trackId, MediaTime start, MediaTime end, TimelineEditMode mode, bool move)
    {
        Apply("Edit subtitle clip", document => ProjectEditingOperations.MoveSubtitleClip(document, subtitleId, trackId, start, end, mode, move));
    }
}
