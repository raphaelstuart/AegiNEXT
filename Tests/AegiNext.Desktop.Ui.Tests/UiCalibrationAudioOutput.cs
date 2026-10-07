using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class UiCalibrationAudioOutput : IAudioOutput
{
    private readonly Lock gate = new();
    private int queued;
    private long epoch;
    private int disposed;
    internal int DisposeCount => Volatile.Read(ref disposed);
    public int LatencyFrames => 0;
    public int QueuedFrames
    {
        get
        {
            lock (gate)
            {
                return queued;
            }
        }
    }

    public AudioOutputClockSnapshot ReadClock()
    {
        lock (gate)
        {
            return new(0, 0, 1, "speaker", "test-system", epoch, AudioClockQuality.SYSTEM, queued);
        }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (gate)
        {
            queued += samples.Length / 2;
        }
    }

    public void SetPaused(bool paused)
    {
    }

    public void Clear()
    {
        lock (gate)
        {
            queued = 0;
            epoch++;
        }
    }

    public void SetGain(float gain)
    {
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposed);
    }
}
