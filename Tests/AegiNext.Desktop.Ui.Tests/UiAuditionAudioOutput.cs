using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class UiAuditionAudioOutput : IAudioOutput
{
    private readonly Lock gate = new();
    private readonly List<float> written = [];
    private int queuedFrames;
    private bool paused = true;
    private int playbackStartCount;
    private int disposeCount;

    public int LatencyFrames => 0;
    public int QueuedFrames
    {
        get
        {
            lock (gate)
            {
                return queuedFrames;
            }
        }
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
    internal int PlaybackStartCount => Volatile.Read(ref playbackStartCount);
    internal int DisposeCount => Volatile.Read(ref disposeCount);
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

    internal void ConsumeAvailable()
    {
        lock (gate)
        {
            if (!paused)
            {
                queuedFrames = 0;
            }
        }
    }

    internal void Consume(int frames)
    {
        lock (gate)
        {
            if (!paused)
            {
                queuedFrames -= Math.Min(frames, queuedFrames);
            }
        }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (gate)
        {
            queuedFrames += samples.Length / 2;
            written.AddRange(samples.ToArray());
            Assert.InRange(queuedFrames, 0, 9600);
        }
    }

    public void SetPaused(bool value)
    {
        lock (gate)
        {
            paused = value;
        }
        if (!value)
        {
            Interlocked.Increment(ref playbackStartCount);
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            queuedFrames = 0;
            written.Clear();
        }
    }

    public void SetGain(float gain)
    {
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposeCount);
    }
}
