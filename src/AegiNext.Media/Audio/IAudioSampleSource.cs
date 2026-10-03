using AegiNext.Core.Timing;

namespace AegiNext.Media.Audio;

/// <summary>
/// 按原始媒体时间流式读取独立 PCM 块；取消会永久终止该源。
/// </summary>
public interface IAudioSampleSource : IDisposable
{
    AudioSampleFormat Format { get; }

    /// <summary>
    /// 读取下一个有界 PCM 块，正常文件结束返回 null。
    /// </summary>
    AudioSampleBlock? Read(CancellationToken cancellationToken = default);

    /// <summary>
    /// 回退解码到目标附近，并裁掉后续输出中早于目标的样本。
    /// </summary>
    void Seek(MediaTime target, CancellationToken cancellationToken = default);

    /// <summary>
    /// 从任意线程请求取消正在进行的解码或定位。
    /// </summary>
    void Cancel();
}
