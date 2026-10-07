using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Desktop.Ui.Tests;

internal sealed class UiRapidZoomAudioSource(long endSample) : IAudioSampleSource
{
    internal const int TONE_HERTZ = 6000;
    private const int BLOCK_FRAMES = 4096;
    private const int PERIOD_FRAMES = 8;
    private static readonly float[] pattern = CreatePattern();
    private long position;
    private long framesRead;
    private int seekCount;
    private int cancelCount;
    private int disposeCount;

    public AudioSampleFormat Format { get; } = new(48000, 1);
    internal Action<CancellationToken>? BeforeRead { get; set; }
    internal long FramesRead => Interlocked.Read(ref framesRead);
    internal int SeekCount => Volatile.Read(ref seekCount);
    internal int CancelCount => Volatile.Read(ref cancelCount);
    internal int DisposeCount => Volatile.Read(ref disposeCount);

    /// <inheritdoc />
    public AudioSampleBlock? Read(CancellationToken cancellationToken = default)
    {
        BeforeRead?.Invoke(cancellationToken);
        CheckState(cancellationToken);
        if (position >= endSample)
        {
            return null;
        }
        var count = (int)Math.Min(BLOCK_FRAMES, endSample - position);
        var samples = new float[count];
        Array.Copy(pattern, (int)(position % PERIOD_FRAMES), samples, 0, count);
        var block = new AudioSampleBlock(Format, new(position, Format.SampleRate), samples);
        position += count;
        Interlocked.Add(ref framesRead, count);
        return block;
    }

    /// <inheritdoc />
    public void Seek(MediaTime target, CancellationToken cancellationToken = default)
    {
        CheckState(cancellationToken);
        position = target.ToTimestamp(new(1, Format.SampleRate), MediaTimeRounding.CEILING).Value;
        Interlocked.Increment(ref seekCount);
    }

    /// <inheritdoc />
    public void Cancel()
    {
        Interlocked.Increment(ref cancelCount);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Interlocked.Increment(ref disposeCount);
    }

    private void CheckState(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(DisposeCount != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        if (CancelCount != 0)
        {
            throw new OperationCanceledException("分析测试源已关闭。");
        }
    }

    private static float[] CreatePattern()
    {
        var samples = new float[BLOCK_FRAMES + PERIOD_FRAMES];
        for (var index = 0; index < PERIOD_FRAMES; index++)
        {
            samples[index] = (float)(0.75 * Math.Sin(index * 2 * Math.PI / PERIOD_FRAMES));
        }
        for (var index = PERIOD_FRAMES; index < samples.Length; index++)
        {
            samples[index] = samples[index % PERIOD_FRAMES];
        }
        return samples;
    }
}
