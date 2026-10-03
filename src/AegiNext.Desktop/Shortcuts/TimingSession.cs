using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Shortcuts;

/// <summary>不可变的字幕进入/退出打轴会话；调用方仅在工程编辑成功后提交返回的新状态。</summary>
public sealed record TimingSession
{
    /// <summary>开始新会话；每次有效进入均创建新字幕。</summary>
    public TimingSession()
    {
    }

    private TimingSession(Guid? activeCueId, MediaTime? activeStart)
    {
        ActiveCueId = activeCueId;
        ActiveStart = activeStart;
    }

    public Guid? ActiveCueId { get; }
    public MediaTime? ActiveStart { get; }

    /// <summary>进入新一句；重复进入不改变开始时间或字幕标识。</summary>
    public TimingEnterResult Enter(MediaTime position)
    {
        if (ActiveCueId is { } activeId)
        {
            return new(this, activeId, ActiveStart!.Value, false, true);
        }

        var cueId = Guid.NewGuid();
        return new(new(cueId, position), cueId, position, true, false);
    }

    /// <summary>结束当前句且保留精确有理时间；未进入时不修改工程，非正时长明确拒绝。</summary>
    public TimingExitResult? Exit(MediaTime position)
    {
        if (ActiveCueId is not { } cueId)
        {
            return null;
        }

        var start = ActiveStart!.Value;
        if (position <= start)
        {
            throw new ArgumentOutOfRangeException(nameof(position), "退出时间必须晚于进入时间。");
        }

        return new(new(), cueId, start, position);
    }

    /// <summary>切换媒体、工程或跳转播放头后重置会话，取消未完成的打轴状态。</summary>
    public TimingSession Reset()
    {
        return ActiveCueId is null ? this : new();
    }
}
