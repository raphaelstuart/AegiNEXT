using System.Collections.Immutable;

namespace AegiNext.Media.Decoding;

/// <summary>
/// 持有独立原生帧引用；帧信息是不可变快照，像素访问与释放按实例串行执行。
/// </summary>
public sealed class DecodedVideoFrame : IVideoFrame
{
    private readonly DecodedFrameHandle handle;
    private readonly Lock gate = new();

    internal unsafe DecodedVideoFrame(DecodedFrameHandle handle)
    {
        this.handle = handle;
        var info = new NativeDecodedFrameInfo { structSize = (uint)sizeof(NativeDecodedFrameInfo), abiVersion = NativeDecodeMethods.ABI_VERSION };
        var hdr = new NativeDecodedHdrInfo { structSize = (uint)sizeof(NativeDecodedHdrInfo), abiVersion = NativeDecodeMethods.ABI_VERSION };
        Span<byte> error = stackalloc byte[NativeDecodeMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* errorPointer = error)
        {
            NativeDecodeError.ThrowIfFailed(NativeDecodeMethods.GetFrameInfo(handle, ref info, errorPointer, (uint)error.Length), error);
            NativeDecodeError.ThrowIfFailed(NativeDecodeMethods.GetHdrInfo(handle, ref hdr, errorPointer, (uint)error.Length), error);
        }

        var names = ImmutableArray.CreateBuilder<string>(checked((int)info.sideDataCount));
        Span<byte> name = stackalloc byte[NativeDecodeMethods.NAME_CAPACITY];
        for (uint index = 0; index < info.sideDataCount; index++)
        {
            name.Clear();
            fixed (byte* namePointer = name)
            fixed (byte* errorPointer = error)
            {
                NativeDecodeError.ThrowIfFailed(NativeDecodeMethods.GetSideDataName(handle, index, namePointer, (uint)name.Length, errorPointer, (uint)error.Length), error);
            }

            names.Add(NativeDecodeError.ReadText(name));
        }

        Info = new(info, hdr, names.ToImmutable());
    }

    public VideoFrameInfo Info { get; }

    /// <summary>
    /// 取得原始 stride 及紧密复制布局；不应用裁剪或像素转换。
    /// </summary>
    public VideoPlaneInfo GetPlaneInfo(int index)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle.IsClosed, this);
            return ReadPlaneInfo(index);
        }
    }

    /// <summary>
    /// 将指定原始平面的有效行复制到独立数组，不含 padding，不改变位深或色彩。
    /// </summary>
    public unsafe byte[] CopyPlane(int index)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle.IsClosed, this);
            var plane = ReadPlaneInfo(index);
            var pixels = new byte[plane.ByteCount];
            Span<byte> error = stackalloc byte[NativeDecodeMethods.ERROR_CAPACITY];
            error.Clear();
            fixed (byte* destination = pixels)
            fixed (byte* errorPointer = error)
            {
                NativeDecodeError.ThrowIfFailed(NativeDecodeMethods.CopyPlane(handle, (uint)index, destination, (ulong)pixels.Length, errorPointer, (uint)error.Length), error);
            }

            return pixels;
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

    internal TResult UseHandle<TResult>(Func<DecodedFrameHandle, TResult> operation)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle.IsClosed, this);
            return operation(handle);
        }
    }

    private unsafe VideoPlaneInfo ReadPlaneInfo(int index)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, Info.PlaneCount);
        var info = new NativeDecodedPlaneInfo { structSize = (uint)sizeof(NativeDecodedPlaneInfo), abiVersion = NativeDecodeMethods.ABI_VERSION };
        Span<byte> error = stackalloc byte[NativeDecodeMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* errorPointer = error)
        {
            NativeDecodeError.ThrowIfFailed(NativeDecodeMethods.GetPlaneInfo(handle, (uint)index, ref info, errorPointer, (uint)error.Length), error);
        }

        var result = new VideoPlaneInfo(checked((int)info.planeIndex), checked((int)info.rowBytes), checked((int)info.rows), info.nativeStride);
        if (info.planeIndex != index || info.tightByteCount != (ulong)result.RowBytes * (ulong)result.Height)
        {
            throw new InvalidDataException("原生视频平面布局不一致。");
        }

        return result;
    }
}
