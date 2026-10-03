namespace AegiNext.Media.Playback;

/// <summary>
/// 视频会话的传输状态；结束状态不推测末帧持续时间。
/// </summary>
public enum VideoPlaybackState
{
    CREATED,
    OPENING,
    PAUSED,
    PLAYING,
    ENDED,
    FAULTED,
    CLOSED
}
