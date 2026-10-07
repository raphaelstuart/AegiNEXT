using System.Collections.Concurrent;
using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class CalibrationAudioSource : IAudioSampleSource
{
    private long nextSample;
    private int disposed;
    public AudioSampleFormat Format { get; } = new();
    internal ConcurrentQueue<MediaTime> SeekTargets { get; } = new();
    internal int DisposeCount => Volatile.Read(ref disposed);

    public AudioSampleBlock Read(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var block = new AudioSampleBlock(Format, new(nextSample, Format.SampleRate), new float[4096]);
        nextSample += 2048;
        return block;
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        nextSample = target.ToTimestamp(new(1, Format.SampleRate), MediaTimeRounding.CEILING).Value;
        SeekTargets.Enqueue(target);
    }

    public void Cancel()
    {
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposed);
    }
}
