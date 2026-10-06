using AegiNext.Media.Audio;

namespace AegiNext.Media.Tests.Audio;

internal sealed class FakeAudioOutput : IAudioOutput
{
    private readonly Lock gate = new();
    private readonly List<float> written = [];
    private int queued;
    internal bool Paused { get; private set; } = true;
    internal int DisposeCount { get; private set; }
    internal float Gain { get; private set; } = 1;
    internal int MaximumQueued { get; private set; }
    public int LatencyFrames { get; init; } = 480;
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

    internal float[] Written
    {
        get
        {
            lock (gate)
            {
                return written.ToArray();
            }
        }
    }

    internal void Consume(int frames)
    {
        lock (gate)
        {
            if (!Paused)
            {
                queued -= Math.Min(queued, frames);
            }
        }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(DisposeCount != 0, this);
            queued += samples.Length / 2;
            Assert.InRange(queued, 0, 12000);
            MaximumQueued = Math.Max(MaximumQueued, queued);
            written.AddRange(samples.ToArray());
        }
    }

    public void SetPaused(bool paused)
    {
        lock (gate)
        {
            Paused = paused;
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            queued = 0;
        }
    }

    public void SetGain(float gain)
    {
        Gain = gain;
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}
