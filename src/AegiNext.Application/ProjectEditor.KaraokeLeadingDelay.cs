using AegiNext.Core.Timing;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>以一个可撤销事务设置句前留白，平移全部逐字时间并保留字幕起止。</summary>
    public void SetKaraokeLeadingDelay(Guid subtitleId, MediaTime delay, MediaTime animationOffset)
    {
        Apply("Edit karaoke leading delay", document => ProjectEditingOperations.SetKaraokeLeadingDelay(
            document, subtitleId, delay, animationOffset));
    }
}
