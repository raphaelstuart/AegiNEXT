using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class UiAuditionAudioSource : IAudioSampleSource
{
    private readonly Lock gate = new();
    private long nextSample;
    private MediaTime lastSeek;
    private int seekCount;
    private int disposeCount;

    public AudioSampleFormat Format { get; } = new();
    internal int SeekCount => Volatile.Read(ref seekCount);
    internal int DisposeCount => Volatile.Read(ref disposeCount);
    internal MediaTime LastSeek
    {
        get
        {
            lock (gate)
            {
                return lastSeek;
            }
        }
    }

    public AudioSampleBlock Read(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var samples = new float[2048];
        for (var index = 0; index < 1024; index++)
        {
            samples[index * 2] = nextSample + index;
            samples[index * 2 + 1] = nextSample + index;
        }
        var block = new AudioSampleBlock(Format, new(nextSample, 48000), samples);
        nextSample += 1024;
        return block;
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (gate)
        {
            lastSeek = target;
        }
        nextSample = target.ToTimestamp(new(1, 48000), MediaTimeRounding.FLOOR).Value;
        Interlocked.Increment(ref seekCount);
    }

    public void Cancel()
    {
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposeCount);
    }
}
