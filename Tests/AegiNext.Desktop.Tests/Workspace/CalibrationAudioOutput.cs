using System.Collections.Concurrent;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class CalibrationAudioOutput(string deviceId) : IAudioOutput
{
    private readonly Lock gate = new();
    private int queued;
    private long played;
    private long epoch;
    private int disposed;
    private bool paused = true;
    private int quality = (int)AudioClockQuality.SYSTEM;
    internal ConcurrentQueue<string> Operations { get; } = new();
    internal int DisposeCount => Volatile.Read(ref disposed);
    internal AudioClockQuality Quality
    {
        get => (AudioClockQuality)Volatile.Read(ref quality);
        set => Volatile.Write(ref quality, (int)value);
    }
    internal bool Paused
    {
        get
        {
            lock (gate)
            {
                return paused;
            }
        }
    }
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
            return new(played, 0, 1, deviceId, "test-system", epoch, Quality, queued);
        }
    }

    internal void Consume(int frames)
    {
        lock (gate)
        {
            Assert.False(paused);
            Assert.InRange(frames, 0, queued);
            played += frames;
            queued -= frames;
        }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (gate)
        {
            queued += samples.Length / 2;
            Assert.InRange(queued, 0, 9600);
        }
    }

    public void SetPaused(bool value)
    {
        lock (gate)
        {
            paused = value;
        }
        Operations.Enqueue(value ? "Pause" : "Play");
    }

    public void Clear()
    {
        lock (gate)
        {
            queued = 0;
            played = 0;
            epoch++;
        }
        Operations.Enqueue("Clear");
    }

    public void SetGain(float gain)
    {
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposed);
    }
}
