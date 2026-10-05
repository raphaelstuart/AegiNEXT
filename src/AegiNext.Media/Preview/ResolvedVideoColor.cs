using AegiNext.Media.Decoding;

namespace AegiNext.Media.Preview;

/// <summary>共享原生媒体核心派生的有效色彩；原始帧事实保持不变。</summary>
public sealed record ResolvedVideoColor
{
    private ResolvedVideoColor(NativeResolvedColor color)
    {
        Range = color.range;
        Matrix = color.matrix;
        Primaries = color.primaries;
        Transfer = color.transfer;
        ChromaLocation = color.chromaLocation;
        AlphaMode = color.alphaMode;
        InferredFields = color.inferredFields;
    }

    public int Range { get; }

    public int Matrix { get; }

    public int Primaries { get; }

    public int Transfer { get; }

    public int ChromaLocation { get; }

    public int AlphaMode { get; }

    /// <summary>缺失字段推断位：范围 1、矩阵 2、基色 4、传递函数 8、色度位置 16。</summary>
    public uint InferredFields { get; }

    /// <summary>在拥有原生引用的帧上解析有效色彩，并保留流级 HDR 证据约束。</summary>
    public static ResolvedVideoColor Resolve(IVideoFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        return frame switch
        {
            DecodedVideoFrame decoded => decoded.UseHandle(ResolveHandle),
            VideoFrameLease lease => lease.UseHandle(ResolveHandle),
            _ => throw new NotSupportedException("色彩解析需要拥有原生引用的解码帧。")
        };
    }

    internal static unsafe ResolvedVideoColor ResolveHandle(DecodedFrameHandle frame)
    {
        FfmpegVideoDecoder.RequireCore();
        var color = new NativeResolvedColor
        {
            structSize = (uint)sizeof(NativeResolvedColor), abiVersion = NativeDecodeMethods.ABI_VERSION
        };
        Span<byte> error = stackalloc byte[NativeDecodeMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* errorPointer = error)
        {
            NativeDecodeError.ThrowIfFailed(NativeDecodeMethods.ResolveColor(frame, ref color, errorPointer, (uint)error.Length), error);
        }
        if (color.coreVersion != NativeDecodeMethods.CORE_VERSION)
        {
            throw new NotSupportedException("原生色彩解析核心版本不匹配。");
        }
        return new(color);
    }
}
