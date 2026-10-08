using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisCacheBuilder
{
    private const int DEFAULT_SEGMENT_SAMPLES = 196608;
    private const long MAXIMUM_BATCH_BYTES = 48L * 1024 * 1024;
    private const int MAXIMUM_SPECTRUM_STRIDE = 1 << 28;
    private readonly Func<CancellationToken, IAudioSampleSource> sourceFactory;
    private readonly MediaTimelineMapping mapping;
    private readonly MediaTime duration;
    private readonly AudioAnalysisCacheStore store;
    private readonly int maximumWorkers;
    private readonly int segmentSamples;

    internal AudioAnalysisCacheBuilder(Func<CancellationToken, IAudioSampleSource> sourceFactory,
        MediaTimelineMapping mapping, MediaTime duration, AudioAnalysisCacheStore store, int maximumWorkers = 0,
        int segmentSamples = DEFAULT_SEGMENT_SAMPLES)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, MediaTime.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumWorkers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segmentSamples);
        if (segmentSamples % (SpectrogramAnalyzer.HOP_SIZE * 3) != 0)
        {
            throw new ArgumentException("分析分段必须对齐媒体频谱步长。", nameof(segmentSamples));
        }
        this.sourceFactory = sourceFactory;
        this.mapping = mapping;
        this.duration = duration;
        this.store = store;
        this.segmentSamples = segmentSamples;
        var workers = maximumWorkers == 0 ? Math.Min(4, Math.Max(1, Environment.ProcessorCount - 2)) : maximumWorkers;
        var rawCount = checked(segmentSamples + AudioSpectrumWindowAnalyzer.RAW_PADDING * 2);
        var segmentBytes = checked((long)rawCount * sizeof(float) +
            (segmentSamples / 3L + SpectrogramAnalyzer.FFT_SIZE) * sizeof(float) +
            segmentSamples / (SpectrogramAnalyzer.HOP_SIZE * 3L) * SpectrogramAnalyzer.FREQUENCY_BINS * 2 + 32768);
        var capacity = (MAXIMUM_BATCH_BYTES - (long)rawCount * sizeof(float) - 256 * 1024) / segmentBytes;
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(segmentSamples), "分段内存超过分析批次预算。");
        }
        this.maximumWorkers = (int)Math.Min(Math.Min(workers, 4), capacity);
    }

    internal async Task BuildAsync(Func<CancellationToken, Task>? checkpoint = null,
        IProgress<AudioAnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!await store.BeginBuildAsync(checkpoint, cancellationToken).ConfigureAwait(false))
            {
                progress?.Report(new(duration, duration, true));
                return;
            }
            using var source = sourceFactory(cancellationToken);
            if (source.Format.SampleRate != WaveformAnalyzer.SAMPLE_RATE || source.Format.Channels != 1)
            {
                throw new InvalidDataException("缓存分析要求 48 kHz 单声道 PCM。");
            }
            using var registration = cancellationToken.UnsafeRegister(static state => ((IAudioSampleSource)state!).Cancel(), source);
            var mediaStart = mapping.Origin.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
            var mediaEnd = mapping.ToMediaTime(duration).ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
            source.Seek(new(mediaStart, WaveformAnalyzer.SAMPLE_RATE), cancellationToken);
            var reader = new AudioAnalysisPcmReader(source, cancellationToken.ThrowIfCancellationRequested, cancellationToken);
            var waveform = new AudioWaveformCacheReducer(store);
            var processors = Enumerable.Range(0, maximumWorkers).Select(_ => new AudioSpectrumSegmentProcessor()).ToArray();
            var cursor = AudioSpectrumWindowAnalyzer.Floor(mediaStart, segmentSamples);
            AudioAnalysisCacheSegment? previous = null;
            while (cursor < Math.Min(mediaEnd, reader.EndSample ?? mediaEnd))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var batch = new List<AudioAnalysisCacheSegment>(maximumWorkers);
                for (var index = 0; index < maximumWorkers && cursor < Math.Min(mediaEnd, reader.EndSample ?? mediaEnd); index++)
                {
                    var segment = ReadSegment(reader, previous, cursor, mediaStart, mediaEnd, cancellationToken);
                    batch.Add(segment);
                    previous = segment;
                    cursor = checked(cursor + segmentSamples);
                }
                var results = await Task.WhenAll(batch.Select((segment, index) => Task.Run(() => segment.Columns == 0 ? [] :
                    processors[index].Analyze(segment.Samples, segment.FirstSample,
                        segment.FirstColumn * SpectrogramAnalyzer.HOP_SIZE, segment.Columns, cancellationToken), cancellationToken)))
                    .ConfigureAwait(false);
                for (var index = 0; index < batch.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var segment = batch[index];
                    waveform.Append(segment.Samples.AsSpan(checked((int)(segment.Start - segment.FirstSample)),
                        checked((int)(segment.End - segment.Start))));
                    AppendSpectrum(segment, results[index], cancellationToken);
                }
                waveform.FlushPages();
                var through = Math.Max(mediaStart, Math.Min(cursor, Math.Min(mediaEnd, reader.EndSample ?? mediaEnd)));
                store.Publish(through, reader.EndSample);
                progress?.Report(new(new(through - mediaStart, WaveformAnalyzer.SAMPLE_RATE), duration, false));
                if (checkpoint is not null)
                {
                    await checkpoint(cancellationToken).ConfigureAwait(false);
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            waveform.Complete();
            var end = Math.Max(mediaStart, Math.Min(mediaEnd, reader.EndSample ?? mediaEnd));
            store.Complete(end);
            progress?.Report(new(new(end - mediaStart, WaveformAnalyzer.SAMPLE_RATE), duration, true));
        }
        catch (Exception error)
        {
            store.Fail(error);
            throw;
        }
    }

    private AudioAnalysisCacheSegment ReadSegment(AudioAnalysisPcmReader reader, AudioAnalysisCacheSegment? previous,
        long cursor, long mediaStart, long mediaEnd, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = checked(cursor - AudioSpectrumWindowAnalyzer.RAW_PADDING);
        var samples = new float[checked(segmentSamples + AudioSpectrumWindowAnalyzer.RAW_PADDING * 2)];
        var start = Math.Max(first, mediaStart);
        var after = Math.Min(checked(first + samples.Length), Math.Min(mediaEnd, reader.EndSample ?? mediaEnd));
        if (previous is not null)
        {
            var overlapEnd = Math.Min(after, checked(previous.FirstSample + previous.Samples.Length));
            if (overlapEnd > start)
            {
                var count = checked((int)(overlapEnd - start));
                previous.Samples.AsSpan(checked((int)(start - previous.FirstSample)), count)
                    .CopyTo(samples.AsSpan(checked((int)(start - first)), count));
                start = overlapEnd;
            }
        }
        if (after > start)
        {
            reader.CopyTo(start, samples.AsSpan(checked((int)(start - first)), checked((int)(after - start))));
        }
        var coreStart = Math.Max(cursor, mediaStart);
        var coreEnd = Math.Max(coreStart, Math.Min(checked(cursor + segmentSamples), Math.Min(mediaEnd, reader.EndSample ?? mediaEnd)));
        var firstColumn = Ceiling(coreStart, SpectrogramAnalyzer.HOP_SIZE * 3L);
        var afterColumn = Ceiling(coreEnd, SpectrogramAnalyzer.HOP_SIZE * 3L);
        return new(first, coreStart, coreEnd, firstColumn, checked((int)(afterColumn - firstColumn)), samples);
    }

    private void AppendSpectrum(AudioAnalysisCacheSegment segment, byte[] basis, CancellationToken cancellationToken)
    {
        if (segment.Columns == 0)
        {
            return;
        }
        store.AppendSpectrum(SpectrogramAnalyzer.HOP_SIZE, segment.FirstColumn, segment.Columns, basis);
        for (var stride = SpectrogramAnalyzer.HOP_SIZE * 2; stride <= MAXIMUM_SPECTRUM_STRIDE; stride *= 2)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ratio = stride / SpectrogramAnalyzer.HOP_SIZE;
            var firstColumn = Ceiling(segment.FirstColumn, ratio);
            var afterColumn = Ceiling(segment.FirstColumn + segment.Columns, ratio);
            var columns = checked((int)(afterColumn - firstColumn));
            if (columns == 0)
            {
                continue;
            }
            var levels = new byte[checked(columns * SpectrogramAnalyzer.FREQUENCY_BINS)];
            for (var column = 0; column < columns; column++)
            {
                var sourceColumn = checked((int)((firstColumn + column) * ratio - segment.FirstColumn));
                for (var row = 0; row < SpectrogramAnalyzer.FREQUENCY_BINS; row++)
                {
                    levels[row * columns + column] = basis[row * segment.Columns + sourceColumn];
                }
            }
            store.AppendSpectrum(stride, firstColumn, columns, levels);
        }
    }

    private static long Ceiling(long value, long step)
    {
        return -AudioSpectrumWindowAnalyzer.Floor(-value, step) / step;
    }
}
