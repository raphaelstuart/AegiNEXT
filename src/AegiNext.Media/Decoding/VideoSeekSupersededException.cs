namespace AegiNext.Media.Decoding;

internal sealed class VideoSeekSupersededException : OperationCanceledException
{
    internal VideoSeekSupersededException() : base("视频定位已被新请求替换。")
    {
    }
}
