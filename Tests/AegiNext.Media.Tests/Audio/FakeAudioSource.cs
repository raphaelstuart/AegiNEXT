using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Tests.Audio;

internal sealed class FakeAudioSource(long firstSample = 4800) : IAudioSampleSource
{
    private bool issued;
    private bool cancelled;
    private long requested;
    public AudioSampleFormat Format { get; } = new();
    internal int DisposeCount { get; private set; }
    internal MediaTime LastSeek { get; private set; }
    internal Action<CancellationToken>? BeforeRead { get; set; }
    internal Action<MediaTime, CancellationToken>? BeforeSeek { get; set; }

    public AudioSampleBlock? Read(CancellationToken cancellationToken = default)
    {
        BeforeRead?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (cancelled)
        {
            throw new OperationCanceledException();
        }

        if (issued)
        {
            return null;
        }

        issued = true;
        var samples = new float[9600];
        Array.Fill(samples, 0.5F);
        return new(Format, new(Math.Max(requested, firstSample), 48000), samples);
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken = default)
    {
        BeforeSeek?.Invoke(target, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        LastSeek = target;
        requested = target.ToTimestamp(new(1, 48000), MediaTimeRounding.CEILING).Value;
        issued = false;
    }

    public void Cancel()
    {
        cancelled = true;
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}
