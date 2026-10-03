using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace AegiNext.Media;

/// <summary>
/// macOS 原生 HDR 预览会话。创建、呈现、验证及正常释放应由 UI 主线程调用。
/// </summary>
[SupportedOSPlatform("macos")]
public sealed class MacHdrPreview : IDisposable
{
    private const int ERROR_CAPACITY = 1024;
    private readonly MacHdrHandle handle;

    private MacHdrPreview(MacHdrHandle handle, nint view)
    {
        this.handle = handle;
        View = view;
    }

    public nint View { get; }

    /// <summary>
    /// 查询存活原生会话数量，用于诊断正常释放是否完成。
    /// </summary>
    public static uint GetLiveContextCount()
    {
        return NativeHdrMethods.LiveContexts();
    }

    /// <summary>
    /// 创建拥有 NSView 的预览会话；调用方仅借用 View，不能另行 release。
    /// </summary>
    public static unsafe MacHdrPreview Create()
    {
        if (NativeHdrMethods.AbiVersion() != NativeHdrMethods.ABI_VERSION)
        {
            throw new InvalidOperationException("原生媒体库 ABI 版本不匹配。");
        }

        Span<byte> error = stackalloc byte[ERROR_CAPACITY];
        error.Clear();
        fixed (byte* errorPointer = error)
        {
            var code = NativeHdrMethods.Create(out var context, out var view, errorPointer, ERROR_CAPACITY);
            ThrowOnFailure(code, error);
            var handle = new MacHdrHandle(context);
            if (handle.IsInvalid || view == 0)
            {
                handle.Dispose();
                throw new InvalidOperationException("原生媒体库未返回有效的视图。");
            }

            return new(handle, view);
        }
    }

    /// <summary>
    /// 同步复制并提交帧；视图尚未附着或不可呈现时返回 null，不保留托管缓冲指针。
    /// </summary>
    public unsafe HdrPreviewStatus? Present(HdrFrame frame)
    {
        ObjectDisposedException.ThrowIf(handle.IsClosed, this);
        ArgumentNullException.ThrowIfNull(frame);
        var status = new NativeHdrStatus
        {
            structSize = (uint)Marshal.SizeOf<NativeHdrStatus>(),
            abiVersion = NativeHdrMethods.ABI_VERSION
        };
        Span<byte> error = stackalloc byte[ERROR_CAPACITY];
        error.Clear();
        fixed (Half* pixels = frame.Pixels)
        fixed (byte* errorPointer = error)
        {
            var code = NativeHdrMethods.Present(handle, pixels, (ulong)frame.Info.ByteCount,
                (uint)frame.Info.Width, (uint)frame.Info.Height, (uint)frame.Info.RowBytes,
                frame.Info.ReferenceWhiteNits, frame.SourcePeakNits, ref status, errorPointer, ERROR_CAPACITY);
            if (code == NativeHdrMethods.NOT_READY)
            {
                return null;
            }

            ThrowOnFailure(code, error);
            return new(status);
        }
    }

    /// <summary>
    /// 执行原生 GPU 上传／读回与离屏颜色变换数值验证。
    /// </summary>
    public unsafe HdrVerificationResult VerifyGpuPipeline()
    {
        ObjectDisposedException.ThrowIf(handle.IsClosed, this);
        var result = new NativeHdrVerification
        {
            structSize = (uint)Marshal.SizeOf<NativeHdrVerification>(),
            abiVersion = NativeHdrMethods.ABI_VERSION
        };
        Span<byte> error = stackalloc byte[ERROR_CAPACITY];
        error.Clear();
        fixed (byte* errorPointer = error)
        {
            var code = NativeHdrMethods.Verify(handle, ref result, errorPointer, ERROR_CAPACITY);
            ThrowOnFailure(code, error);
            return new(result);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        handle.Dispose();
    }

    private static void ThrowOnFailure(int code, ReadOnlySpan<byte> error)
    {
        if (code == 0)
        {
            return;
        }

        var terminator = error.IndexOf((byte)0);
        var message = System.Text.Encoding.UTF8.GetString(terminator < 0 ? error : error[..terminator]);
        throw new InvalidOperationException($"原生 HDR 预览错误 {code}：{message}");
    }
}
