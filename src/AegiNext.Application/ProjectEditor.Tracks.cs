using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>新增具有稳定身份的轨道，作为一个可撤销事务。</summary>
    public Guid AddTrack(string name)
    {
        var track = new ProjectTrack { Name = name };
        Apply("Add track", document => ProjectEditingOperations.AddTrack(document, track));
        return track.Id;
    }

    /// <summary>重命名轨道，保留片段与效果身份。</summary>
    public void RenameTrack(Guid trackId, string name)
    {
        Apply("Rename track", document => ProjectEditingOperations.RenameTrack(document, trackId, name));
    }

    /// <summary>设置轨道后续创建默认样式；明确启用时才同步轨道现有字幕。</summary>
    public void SetSubtitleTrackStyle(Guid trackId, Guid presetId, string presetName, SubtitleStyle style, bool updateExisting = false)
    {
        Apply("Apply subtitle track style", document =>
            ProjectEditingOperations.SetSubtitleTrackStyle(document, trackId, presetId, presetName, style, updateExisting));
    }

    /// <summary>切换轨道新字幕的自动样式，保留轨道快照及现有字幕。</summary>
    public void SetSubtitleTrackAutoApplyStyle(Guid trackId, bool enabled)
    {
        Apply("Toggle subtitle track automatic style", document =>
            ProjectEditingOperations.SetSubtitleTrackAutoApplyStyle(document, trackId, enabled));
    }

    /// <summary>在一个可撤销事务中删除轨道及其全部片段；允许工程暂时没有轨道。</summary>
    public void RemoveTrack(Guid trackId)
    {
        Apply("Remove track", document => ProjectEditingOperations.RemoveTrack(document, trackId));
    }

    /// <summary>调整轨道显示及叠覆顺序，片段稳定身份保持原样。</summary>
    public void MoveTrack(Guid trackId, int newIndex)
    {
        Apply("Reorder tracks", document => ProjectEditingOperations.MoveTrack(document, trackId, newIndex));
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
    /// <summary>在一个可撤销事务中调整任意类型片段的轨道和时间。</summary>
    public void MoveClip(Guid clipId, Guid trackId, MediaTime start, MediaTime end, TimelineEditMode mode, bool move)
    {
        Apply("Edit clip", document => ProjectEditingOperations.MoveClip(document, clipId, trackId, start, end, mode, move));
    }
}
