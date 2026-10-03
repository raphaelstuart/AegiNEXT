namespace AegiNext.Media.Audio;

/// <summary>
/// 默认 SDL3 音频设备的有界输出；设备缓冲块仅提供延迟估计，不代表 DAC 精确播放游标。
/// </summary>
public sealed class SdlAudioOutput : IAudioOutput
{
    private readonly AudioOutputHandle handle;

    /// <summary>创建默认设备，初始暂停，源格式固定为 48kHz float32 立体声。</summary>
    public unsafe SdlAudioOutput()
    {
        Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* text = error)
        {
            NativeAudioError.Check(NativeAudioMethods.CreateOutput(out var pointer, 48000, 2, text, (uint)error.Length), error);
            handle = new(pointer);
        }
    }

    public int QueuedFrames => RequireCount(NativeAudioMethods.Queued(handle));
    public int LatencyFrames => RequireCount(NativeAudioMethods.Latency(handle));

    /// <inheritdoc />
    public unsafe void Write(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0 || samples.Length % 2 != 0)
        {
            throw new ArgumentException("输出必须包含完整的立体声样本帧。", nameof(samples));
        }

        Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (float* data = samples)
        fixed (byte* text = error)
        {
            NativeAudioError.Check(NativeAudioMethods.Write(handle, data, samples.Length / 2, text, (uint)error.Length), error);
        }
    }

    /// <inheritdoc />
    public unsafe void SetPaused(bool paused)
    {
        Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* text = error)
        {
            NativeAudioError.Check(NativeAudioMethods.Pause(handle, paused ? 1 : 0, text, (uint)error.Length), error);
        }
    }

    /// <inheritdoc />
    public unsafe void Clear()
    {
        Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* text = error)
        {
            NativeAudioError.Check(NativeAudioMethods.Clear(handle, text, (uint)error.Length), error);
        }
    }

    /// <inheritdoc />
    public unsafe void SetGain(float gain)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(gain);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(gain, 1);
        Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* text = error)
        {
            NativeAudioError.Check(NativeAudioMethods.Gain(handle, gain, text, (uint)error.Length), error);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        handle.Dispose();
    }

    private static int RequireCount(int count)
    {
        return count >= 0 ? count : throw new IOException("无法读取音频设备队列状态。");
    }
}
