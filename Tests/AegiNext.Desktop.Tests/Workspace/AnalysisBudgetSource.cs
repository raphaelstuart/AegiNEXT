using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Tests.Workspace;

internal sealed class AnalysisBudgetSource(long endSample) : IAudioSampleSource
{
    private long position;
    private long framesRead;
    private int cancelCount;
    private int disposeCount;

    public AudioSampleFormat Format { get; } = new(WaveformAnalyzer.SAMPLE_RATE, 1);
    internal long FramesRead => Interlocked.Read(ref framesRead);
    internal int CancelCount => Volatile.Read(ref cancelCount);
    internal int DisposeCount => Volatile.Read(ref disposeCount);
    internal long MaximumFrames { get; init; } = 10L * WaveformAnalyzer.SAMPLE_RATE;
    internal Action<CancellationToken>? BeforeRead { get; init; }

    public AudioSampleBlock? Read(CancellationToken cancellationToken = default)
    {
        BeforeRead?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (position >= endSample)
        {
            return null;
        }
        var count = (int)Math.Min(4096, endSample - position);
        if (Interlocked.Add(ref framesRead, count) > MaximumFrames)
        {
            throw new InvalidDataException("Analysis exceeded the local viewport I/O budget.");
        }
        var block = new AudioSampleBlock(Format, new(position, Format.SampleRate), new float[count]);
        position += count;
        return block;
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        position = Math.Max(0, target.ToTimestamp(new(1, Format.SampleRate), MediaTimeRounding.CEILING).Value);
    }

    public void Cancel()
    {
        Interlocked.Increment(ref cancelCount);
    }

    public void Dispose()
    {
        Interlocked.Increment(ref disposeCount);
    }
}
