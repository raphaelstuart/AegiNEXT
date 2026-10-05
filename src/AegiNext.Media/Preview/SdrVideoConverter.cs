using System.Globalization;
using AegiNext.Media.Decoding;

namespace AegiNext.Media.Preview;

/// <summary>
/// 以固定 FFmpeg CPU 色彩管理派生 sRGB BGRA8；感知映射只用于显示，不能回流 HDR 导出。
/// </summary>
public sealed class SdrVideoConverter : IVideoPreviewConverter
{
    private readonly Lock gate = new();
    private readonly VideoPreviewHandle handle;
    private readonly SdrPreviewOptions options;

    /// <summary>
    /// 验证原生能力与库版本并创建串行转换器；不读取媒体或修改开发环境。
    /// </summary>
    public unsafe SdrVideoConverter(SdrPreviewOptions? options = null)
    {
        this.options = options ?? new();
        _ = GetBackendInfo();
        Span<byte> error = stackalloc byte[NativeDecodeMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* errorPointer = error)
        {
            NativeDecodeError.ThrowIfFailed(NativePreviewMethods.Create(out var pointer, errorPointer, (uint)error.Length), error);
            handle = new(pointer);
        }
    }

    public static uint LiveConverterCount => NativePreviewMethods.LiveConverters();

    /// <summary>
    /// 核验 ABI、SDR 预览能力和编译／运行版本；旧原生库明确拒绝，不静默换后端。
    /// </summary>
    public static unsafe SdrPreviewBackendInfo GetBackendInfo()
    {
        try
        {
            if (NativeDecodeMethods.AbiVersion() != NativeDecodeMethods.ABI_VERSION ||
                (NativeDecodeMethods.Features() & NativePreviewMethods.FEATURE) == 0)
            {
                throw new NotSupportedException("原生解码库缺少 SDR 预览能力，请执行 Decoder 构建。");
            }

            FfmpegVideoDecoder.RequireCore();
            var info = new NativePreviewBackendInfo
            {
                structSize = (uint)sizeof(NativePreviewBackendInfo),
                abiVersion = NativeDecodeMethods.ABI_VERSION
            };
            Span<byte> error = stackalloc byte[NativeDecodeMethods.ERROR_CAPACITY];
            error.Clear();
            fixed (byte* errorPointer = error)
            {
                NativeDecodeError.ThrowIfFailed(NativePreviewMethods.GetBackendInfo(ref info, errorPointer, (uint)error.Length), error);
            }

            if (info.compileSwscale != info.runtimeSwscale)
            {
                throw new NotSupportedException("SDR 转换库编译与运行版本不一致。");
            }

            return new(FormatVersion(info.compileSwscale), FormatVersion(info.runtimeSwscale));
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new NotSupportedException("无法加载 SDR 预览后端；请为当前平台执行 Decoder 构建。", exception);
        }
    }

    /// <inheritdoc />
    public SdrVideoFrame Convert(IVideoFrame frame, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(frame);
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle.IsClosed, this);
            if (frame is not DecodedVideoFrame and not VideoFrameLease)
            {
                throw new NotSupportedException("此转换器需要拥有原生引用的解码帧。");
            }

            var (width, height) = GetOutputSize(frame.Info);
            var pixels = new byte[checked(width * height * 4)];
            if (frame is DecodedVideoFrame decoded)
            {
                decoded.UseHandle(frameHandle => Render(frameHandle, ResolvedVideoColor.ResolveHandle(frameHandle), width, height, pixels));
            }
            else
            {
                ((VideoFrameLease)frame).UseHandle(frameHandle => Render(frameHandle, ResolvedVideoColor.ResolveHandle(frameHandle), width, height, pixels));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return SdrVideoFrame.FromOwnedPixels(width, height, pixels);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            handle.Dispose();
        }
    }

    private (int Width, int Height) GetOutputSize(VideoFrameInfo frame)
    {
        var width = frame.Width - frame.CropLeft - frame.CropRight;
        var height = frame.Height - frame.CropTop - frame.CropBottom;
        var sar = frame.SampleAspectRatio;
        var displayWidth = width * (sar is null ? 1.0 : (double)sar.Numerator / sar.Denominator);
        var scale = Math.Min(1, Math.Min(options.MaximumWidth / displayWidth, (double)options.MaximumHeight / height));
        return (Math.Clamp((int)Math.Round(displayWidth * scale), 1, options.MaximumWidth),
            Math.Clamp((int)Math.Round(height * scale), 1, options.MaximumHeight));
    }

    private unsafe bool Render(DecodedFrameHandle frame, ResolvedVideoColor color, int width, int height, byte[] pixels)
    {
        var request = new NativePreviewRequest
        {
            structSize = (uint)sizeof(NativePreviewRequest),
            abiVersion = NativeDecodeMethods.ABI_VERSION,
            width = (uint)width,
            height = (uint)height,
            colorRange = color.Range,
            colorMatrix = color.Matrix,
            colorPrimaries = color.Primaries,
            colorTransfer = color.Transfer,
            chromaLocation = color.ChromaLocation,
            alphaMode = color.AlphaMode
        };
        Span<byte> error = stackalloc byte[NativeDecodeMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* destination = pixels)
        fixed (byte* errorPointer = error)
        {
            NativeDecodeError.ThrowIfFailed(NativePreviewMethods.Convert(handle, frame, ref request, destination,
                (ulong)pixels.Length, errorPointer, (uint)error.Length), error);
        }

        return true;
    }

    private static string FormatVersion(uint value)
    {
        return string.Create(CultureInfo.InvariantCulture, $"{value >> 16}.{(value >> 8) & 255}.{value & 255}");
    }
}
