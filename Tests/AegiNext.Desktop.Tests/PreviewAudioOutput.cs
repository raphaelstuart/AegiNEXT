using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests;

internal sealed class PreviewAudioOutput : IAudioOutput
{
    private readonly Lock gate = new();
    private int queued;
    private readonly List<float> written = [];
    internal bool Paused { get; private set; } = true;
    internal float Gain { get; private set; } = 1;
    internal int DisposeCount { get; private set; }
    internal Action? PlaybackStarted { get; set; }
    internal bool ThrowOnPause { get; set; }
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
    public int LatencyFrames => 480;
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
            queued += samples.Length / 2;
            written.AddRange(samples.ToArray());
            Assert.InRange(queued, 0, 12000);
        }
    }

    public void SetPaused(bool paused)
    {
        lock (gate)
        {
            if (paused && ThrowOnPause)
            {
                throw new InvalidOperationException("输出设备暂停失败。");
            }
            Paused = paused;
            if (!paused)
            {
                PlaybackStarted?.Invoke();
            }
        }
    }

    public void Clear()
    {
        lock (gate)
        {
            queued = 0;
            written.Clear();
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
