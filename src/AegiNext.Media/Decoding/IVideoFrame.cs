namespace AegiNext.Media.Decoding;

/// <summary>
/// 由调用方释放的独立原始视频帧；信息与像素格式不包含显示转换。
/// </summary>
public interface IVideoFrame : IDisposable
{
    VideoFrameInfo Info { get; }

    /// <summary>
    /// 查询指定平面的原始与紧密复制布局。
    /// </summary>
    VideoPlaneInfo GetPlaneInfo(int index);

    /// <summary>
    /// 复制有效像素行到调用方拥有的独立数组。
    /// </summary>
    byte[] CopyPlane(int index);
}
