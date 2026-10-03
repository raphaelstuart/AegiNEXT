using System.Diagnostics.CodeAnalysis;
using AegiNext.Core.Timing;

namespace AegiNext.Media.Audio;

/// <summary>
/// 固定 FFmpeg 后端的同步流式 PCM 解码器；输出块独立，取消永久终止当前实例。
/// </summary>
public sealed class FfmpegAudioDecoder : IAudioSampleSource
{
    private readonly AudioDecoderHandle handle;
    private readonly Lock gate = new();

    private FfmpegAudioDecoder(AudioDecoderHandle handle, AudioSampleFormat format)
    {
        this.handle = handle;
        Format = format;
    }

    public AudioSampleFormat Format { get; }

    /// <summary>
    /// 打开本地文件绝对流索引，默认转换到 48kHz 立体声；分析可请求 16kHz 单声道。
    /// </summary>
    public static unsafe FfmpegAudioDecoder Open(string path, int streamIndex,
        AudioSampleFormat? format = null, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
        cancellationToken.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException("音频文件不存在。", path);
        }

        if (NativeAudioMethods.AbiVersion() != 1)
        {
            throw new InvalidOperationException("原生音频 ABI 版本不匹配。");
        }

        format ??= new();
        Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* text = error)
        {
            NativeAudioError.Check(NativeAudioMethods.CreateDecoder(out var pointer, text, (uint)error.Length), error, cancellationToken);
            var handle = new AudioDecoderHandle(pointer);
            try
            {
                using var registration = cancellationToken.UnsafeRegister(static state => NativeAudioMethods.Cancel((AudioDecoderHandle)state!), handle);
                NativeAudioError.Check(NativeAudioMethods.OpenDecoder(handle, path, streamIndex, format.SampleRate, format.Channels, text, (uint)error.Length), error, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return new(handle, format);
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }
    }

    /// <inheritdoc />
    public unsafe AudioSampleBlock? Read(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle.IsClosed, this);
            var samples = new float[4096 * Format.Channels];
            Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
            error.Clear();
            using var registration = cancellationToken.UnsafeRegister(static state => NativeAudioMethods.Cancel((AudioDecoderHandle)state!), handle);
            fixed (float* data = samples)
            fixed (byte* text = error)
            {
                var status = NativeAudioMethods.Read(handle, data, 4096, out var frames, out var start, text, (uint)error.Length);
                cancellationToken.ThrowIfCancellationRequested();
                if (status == 1)
                {
                    return null;
                }

                NativeAudioError.Check(status, error, cancellationToken);
                return new(Format, new(start, Format.SampleRate), samples.AsSpan(0, checked(frames * Format.Channels)));
            }
        }
    }

    /// <inheritdoc />
    public unsafe void Seek(MediaTime target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(handle.IsClosed, this);
            var sample = target.ToTimestamp(new(1, Format.SampleRate), MediaTimeRounding.CEILING).Value;
            Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
            error.Clear();
            using var registration = cancellationToken.UnsafeRegister(static state => NativeAudioMethods.Cancel((AudioDecoderHandle)state!), handle);
            fixed (byte* text = error)
            {
                NativeAudioError.Check(NativeAudioMethods.Seek(handle, sample, text, (uint)error.Length), error, cancellationToken);
            }
        }
    }

    /// <inheritdoc />
    [SuppressMessage("ReSharper", "InconsistentlySynchronizedField", Justification = "Native cancellation only sets an atomic flag; SafeHandle pins its lifetime while a read holds the gate.")]
    public void Cancel()
    {
        NativeAudioMethods.Cancel(handle);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (!handle.IsClosed)
            {
                NativeAudioMethods.Cancel(handle);
            }
        }
        catch (ObjectDisposedException)
        {
        }

        lock (gate)
        {
            handle.Dispose();
        }
    }
}
