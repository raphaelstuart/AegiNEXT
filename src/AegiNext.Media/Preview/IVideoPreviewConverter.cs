using AegiNext.Media.Decoding;

namespace AegiNext.Media.Preview;

/// <summary>
/// 从借用的原始帧派生独立 SDR 显示图像；不取得源帧所有权，不提供编码输入。
/// </summary>
public interface IVideoPreviewConverter : IDisposable
{
    /// <summary>
    /// 同步转换借用帧；调用者负责在后台串行执行，并在返回后释放源帧。
    /// </summary>
    SdrVideoFrame Convert(IVideoFrame frame, CancellationToken cancellationToken = default);

    /// <summary>返回准备图像及其保留的附属像素内存；合成器应包含独立背景图像并去重共享数组。</summary>
    long GetRetainedBytes(SdrVideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame.Pixels.Length;
    }
}
