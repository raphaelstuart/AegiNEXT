using AegiNext.Media.Audio;

namespace AegiNext.Media.Tests.Audio;

internal sealed class ClockAudioOutput : IAudioOutput
{
    private readonly Lock gate = new();
    private int queued;
    private long epoch;
    internal long PlayedFrames { get; set; }
    internal AudioClockQuality Quality { get; set; } = AudioClockQuality.SYSTEM;
    internal string DeviceId { get; set; } = "test-device";
    internal bool Paused { get; private set; } = true;
    internal void ChangeEpoch() => epoch++;
    public int LatencyFrames => 4800;
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
            return new(PlayedFrames, PlayedFrames, 48000, DeviceId, "test-system", epoch, Quality, queued);
        }
    }

    public void Write(ReadOnlySpan<float> samples)
    {
        lock (gate)
        {
            queued += samples.Length / 2;
        }
    }

    public void SetPaused(bool paused) => Paused = paused;

    public void Clear()
    {
        lock (gate)
        {
            queued = 0;
            PlayedFrames = 0;
            epoch++;
        }
    }

    public void SetGain(float gain)
    {
    }

    public void Dispose()
    {
    }
}
