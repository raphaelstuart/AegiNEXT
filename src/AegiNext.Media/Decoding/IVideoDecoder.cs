using AegiNext.Core.Timing;

namespace AegiNext.Media.Decoding;

/// <summary>
/// 顺序解码与关键帧定位契约；除取消外的操作由持有者串行执行。
/// </summary>
public interface IVideoDecoder : IDisposable
{
    MediaTimeBase StreamTimeBase { get; }

    /// <summary>
    /// 读取独立原始帧，正常 EOF 返回 null。
    /// </summary>
    IVideoFrame? ReadFrame(CancellationToken cancellationToken = default);

    /// <summary>
    /// 按原始媒体时间向前定位关键帧；后续仍需解码选择显示帧。
    /// </summary>
    void SeekToKeyFrame(MediaTime target, CancellationToken cancellationToken = default);

    /// <summary>
    /// 请求终端协作取消，可以与读取或定位并发。
    /// </summary>
    void Cancel();
}
