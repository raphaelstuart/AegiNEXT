using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Tests.Analysis;

internal sealed class WindowAudioSource(Func<long, float> sample, long endSample, int blockSize = 4096,
    long firstSample = 0) : IAudioSampleSource
{
    private long position = firstSample;
    private readonly long minimumSample = firstSample;
    private int cancelled;
    internal int SeekCount { get; private set; }
    internal int ReadCount { get; private set; }
    internal long FramesRead { get; private set; }
    internal long? FirstReadSample { get; private set; }
    internal long? LastReadSample { get; private set; }
    internal int CancelCount { get; private set; }
    internal int DisposeCount { get; private set; }
    internal Action<CancellationToken>? BeforeRead { get; set; }
    internal List<MediaTime> SeekTargets { get; } = [];
    internal (long Start, long End)? Gap { get; set; }
    public AudioSampleFormat Format { get; } = new(WaveformAnalyzer.SAMPLE_RATE, 1);

    public AudioSampleBlock? Read(CancellationToken cancellationToken = default)
    {
        BeforeRead?.Invoke(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (Volatile.Read(ref cancelled) != 0)
        {
            throw new OperationCanceledException();
        }
        ReadCount++;
        if (Gap is { } gap && position >= gap.Start && position < gap.End)
        {
            position = gap.End;
        }
        if (position >= endSample)
        {
            return null;
        }
        var count = (int)Math.Min(blockSize, endSample - position);
        if (Gap is { } nextGap && position < nextGap.Start)
        {
            count = (int)Math.Min(count, nextGap.Start - position);
        }
        var data = new float[count];
        for (var index = 0; index < count; index++)
        {
            data[index] = sample(position + index);
        }
        var result = new AudioSampleBlock(Format, new(position, Format.SampleRate), data);
        FirstReadSample ??= position;
        LastReadSample = Math.Max(LastReadSample ?? position, position + count);
        FramesRead += count;
        position += count;
        return result;
    }

    public void Seek(MediaTime target, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        position = Math.Max(minimumSample, target.ToTimestamp(new(1, Format.SampleRate), MediaTimeRounding.CEILING).Value);
        SeekCount++;
        SeekTargets.Add(target);
    }

    public void Cancel()
    {
        Interlocked.Exchange(ref cancelled, 1);
        CancelCount++;
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}
