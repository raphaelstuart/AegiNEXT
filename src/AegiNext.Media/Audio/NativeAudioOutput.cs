namespace AegiNext.Media.Audio;

/// <summary>
/// 原生输出设备的有界 PCM 写入与原子时钟快照。
/// </summary>
public abstract class NativeAudioOutput : IAudioOutput
{
    private readonly AudioOutputHandle handle;

    /// <summary>创建默认设备，初始暂停，源格式固定为 48kHz float32 立体声。</summary>
    protected unsafe NativeAudioOutput(bool system)
    {
        if (NativeAudioMethods.AbiVersion() != 2 || NativeAudioMethods.ClockSnapshotSize() != sizeof(NativeAudioClock))
        {
            throw new NotSupportedException("原生音频 ABI 与托管绑定不一致。");
        }
        Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* text = error)
        {
            nint pointer;
            var result = system
                ? NativeAudioMethods.CreateSystemOutput(out pointer, 48000, 2, text, (uint)error.Length)
                : NativeAudioMethods.CreateOutput(out pointer, 48000, 2, text, (uint)error.Length);
            NativeAudioError.Check(result, error);
            handle = new(pointer);
        }
    }

    public int QueuedFrames => ReadClock().QueuedFrames;
    public int LatencyFrames => RequireCount(NativeAudioMethods.Latency(handle));

    /// <inheritdoc />
    public unsafe AudioOutputClockSnapshot ReadClock()
    {
        var snapshot = new NativeAudioClock { Size = (uint)sizeof(NativeAudioClock) };
        Span<byte> error = stackalloc byte[NativeAudioMethods.ERROR_CAPACITY];
        error.Clear();
        fixed (byte* text = error)
        {
            NativeAudioError.Check(NativeAudioMethods.Snapshot(handle, ref snapshot, text, (uint)error.Length), error);
        }
        return snapshot.ToSnapshot();
    }

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
        GC.SuppressFinalize(this);
    }

    private static int RequireCount(int count)
    {
        return count >= 0 ? count : throw new IOException("无法读取音频设备队列状态。");
    }
}
