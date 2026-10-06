using AegiNext.Core.Timing;
using AegiNext.Media.Playback;
using AegiNext.Media.Decoding;

namespace AegiNext.Desktop.Controllers;

/// <summary>
/// 桌面预览的不可变状态；媒体位置保持原始时间轴，界面可减去起点显示相对时间。
/// 呈现测量只记录当前请求身份下已成功交付的帧；呈现时刻使用同一个原始媒体时钟。
/// </summary>
public sealed record VideoPreviewSnapshot(string? FilePath, VideoPlaybackState State, bool IsOpening,
    MediaTime Position, MediaTime? Start, MediaTime? Duration, Exception? Error, long Epoch,
    MediaTime? PresentedFrameTime = null, MediaTime? PresentedAtPosition = null, long? PresentedGeneration = null,
    bool AudioAvailable = false, Exception? AudioError = null, float Volume = 1, bool IsMuted = false,
    VideoDecodeSessionInfo? DecodeSessionInfo = null, bool AudioAuditionActive = false);
