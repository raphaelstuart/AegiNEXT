using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

internal sealed class AudioAnalysisCacheBuilder
{
    private const int DEFAULT_SEGMENT_SAMPLES = 196608;
    private const int MAXIMUM_SPECTRUM_STRIDE = 1 << 28;
    private readonly Lock executionGate = new();
    private readonly object workerOwner = new();
    private readonly Func<CancellationToken, IAudioSampleSource> sourceFactory;
    private readonly MediaTimelineMapping mapping;
    private readonly MediaTime duration;
    private readonly AudioAnalysisCacheStore store;
    private readonly AudioAnalysisRecipe recipe;
    private readonly AudioAnalysisWorkerBudget? sharedBudget;
    private AudioAnalysisExecutionOptions execution;
    private AudioAnalysisExecutionOptions? pendingExecution;
    private int effectiveWorkers;
    private long plannedWorkingBytes;

    internal AudioAnalysisCacheBuilder(Func<CancellationToken, IAudioSampleSource> sourceFactory,
        MediaTimelineMapping mapping, MediaTime duration, AudioAnalysisCacheStore store, int maximumWorkers = 0,
        int segmentSamples = DEFAULT_SEGMENT_SAMPLES, AudioAnalysisOptions? options = null,
        AudioAnalysisWorkerBudget? budget = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, MediaTime.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumWorkers);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(segmentSamples);
        options?.Validate();
        this.sourceFactory = sourceFactory;
        this.mapping = mapping;
        this.duration = duration;
        this.store = store;
        recipe = store.Recipe;
        if (options is not null && options.Recipe != recipe)
        {
            throw new ArgumentException("缓存与生成器必须使用相同的分析配方。", nameof(options));
        }
        execution = options?.Execution ?? new() { MaximumWorkers = maximumWorkers, SegmentSamples = segmentSamples };
        sharedBudget = budget;
        ValidateSegment(execution);
        UpdateCapacity(execution, budget?.MaximumWorkers ?? execution.EffectiveMaximumWorkers);
    }

    internal int EffectiveWorkers => Volatile.Read(ref effectiveWorkers);
    internal long PlannedWorkingBytes => Interlocked.Read(ref plannedWorkingBytes);

    internal void UpdateExecutionOptions(AudioAnalysisExecutionOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        ValidateSegment(value);
        CalculateCapacity(value, sharedBudget?.MaximumWorkers ?? value.EffectiveMaximumWorkers);
        lock (executionGate)
        {
            pendingExecution = value;
        }
    }

    internal async Task BuildAsync(Func<CancellationToken, Task>? checkpoint = null,
        IProgress<AudioAnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var ownedBudget = sharedBudget is null ? new AudioAnalysisWorkerBudget(execution.MaximumWorkers) : null;
        var budget = sharedBudget ?? ownedBudget!;
        var token = lifetime.Token;
        try
        {
            token.ThrowIfCancellationRequested();
            if (!await store.BeginBuildAsync(checkpoint, token).ConfigureAwait(false))
            {
                progress?.Report(new(duration, duration, true));
                return;
            }
            using var sourceLifetime = new AudioAnalysisSourceLifetime(await OpenSourceAsync(budget, token).ConfigureAwait(false), token);
            var source = sourceLifetime.Source;
            if (source.Format.SampleRate != WaveformAnalyzer.SAMPLE_RATE || source.Format.Channels != 1)
            {
                throw new InvalidDataException("缓存分析要求 48 kHz 单声道 PCM。");
            }
            var mediaStart = mapping.Origin.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
            var mediaEnd = mapping.ToMediaTime(duration).ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
            using (await budget.AcquireAsync(workerOwner, cancellationToken: token).ConfigureAwait(false))
            {
                source.Seek(new(mediaStart, WaveformAnalyzer.SAMPLE_RATE), token);
            }
            var reader = new AudioAnalysisPcmReader(source, token.ThrowIfCancellationRequested, token);
            var waveform = new AudioWaveformCacheReducer(store);
            var overlap = new AudioAnalysisPcmOverlap(recipe.RawPadding * 2);
            var cursor = AudioSpectrumWindowAnalyzer.Floor(mediaStart, execution.SegmentSamples);
            AudioAnalysisPipelineBuffers? buffers = null;
            while (cursor < Math.Min(mediaEnd, reader.EndSample ?? mediaEnd))
            {
                token.ThrowIfCancellationRequested();
                ApplyPendingExecution(ownedBudget);
                UpdateCapacity(execution, budget.MaximumWorkers);
                if (buffers is null || buffers.Workers != EffectiveWorkers || buffers.SegmentSamples != execution.SegmentSamples)
                {
                    buffers = null;
                    buffers = new(EffectiveWorkers, execution.SegmentSamples, recipe);
                }
                cursor = await ProcessEpochAsync(reader, overlap, buffers, waveform, cursor, mediaStart, mediaEnd,
                    budget, lifetime).ConfigureAwait(false);
                using (await budget.AcquireAsync(workerOwner, cancellationToken: token).ConfigureAwait(false))
                {
                    waveform.FlushPages();
                    var through = Math.Max(mediaStart, Math.Min(cursor, Math.Min(mediaEnd, reader.EndSample ?? mediaEnd)));
                    store.Publish(through, reader.EndSample);
                    progress?.Report(new(new(through - mediaStart, WaveformAnalyzer.SAMPLE_RATE), duration, false));
                }
                if (checkpoint is not null)
                {
                    await checkpoint(token).ConfigureAwait(false);
                }
            }
            using (await budget.AcquireAsync(workerOwner, cancellationToken: token).ConfigureAwait(false))
            {
                token.ThrowIfCancellationRequested();
                waveform.Complete();
                var end = Math.Max(mediaStart, Math.Min(mediaEnd, reader.EndSample ?? mediaEnd));
                store.Complete(end);
                progress?.Report(new(new(end - mediaStart, WaveformAnalyzer.SAMPLE_RATE), duration, true));
            }
        }
        catch (Exception error)
        {
            lifetime.Cancel();
            store.Fail(error);
            throw;
        }
    }

    private async Task<IAudioSampleSource> OpenSourceAsync(AudioAnalysisWorkerBudget budget, CancellationToken token)
    {
        using var lease = await budget.AcquireAsync(workerOwner, cancellationToken: token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        return sourceFactory(token);
    }

    private async Task<long> ProcessEpochAsync(AudioAnalysisPcmReader reader, AudioAnalysisPcmOverlap overlap,
        AudioAnalysisPipelineBuffers buffers, AudioWaveformCacheReducer waveform, long cursor, long mediaStart,
        long mediaEnd, AudioAnalysisWorkerBudget budget, CancellationTokenSource lifetime)
    {
        var queue = Channel.CreateBounded<AudioAnalysisSegmentWork>(new BoundedChannelOptions(buffers.Workers)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });
        var computations = new List<Task<ReadOnlyMemory<byte>>>(buffers.Workers);
        var producer = Task.Run(ProduceAsync, lifetime.Token);
        var writer = CommitAsync();
        try
        {
            await Task.WhenAll(producer, writer).ConfigureAwait(false);
            return await producer.ConfigureAwait(false);
        }
        catch (Exception error)
        {
            lifetime.Cancel();
            var cause = producer.Exception?.InnerException;
            if (cause is null or OperationCanceledException)
            {
                cause = writer.Exception?.InnerException ?? error;
            }
            ExceptionDispatchInfo.Capture(cause).Throw();
            throw;
        }
        finally
        {
            await ((Task)Task.WhenAll(computations)).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        async Task<long> ProduceAsync()
        {
            try
            {
                for (var index = 0; index < buffers.Workers && cursor < Math.Min(mediaEnd, reader.EndSample ?? mediaEnd); index++)
                {
                    AudioAnalysisCacheSegment segment;
                    using (await budget.AcquireAsync(workerOwner, cancellationToken: lifetime.Token).ConfigureAwait(false))
                    {
                        segment = ReadSegment(reader, overlap, buffers.RawSamples[index], cursor, mediaStart, mediaEnd,
                            buffers.SegmentSamples, lifetime.Token);
                    }
                    var computation = AnalyzeAsync(segment, buffers.Processors[index], buffers.Levels[index], budget, lifetime.Token);
                    computations.Add(computation);
                    await queue.Writer.WriteAsync(new(segment, computation), lifetime.Token).ConfigureAwait(false);
                    cursor = checked(cursor + buffers.SegmentSamples);
                }
                queue.Writer.TryComplete();
                return cursor;
            }
            catch (Exception error)
            {
                queue.Writer.TryComplete(error);
                lifetime.Cancel();
                throw;
            }
        }

        async Task CommitAsync()
        {
            try
            {
                await foreach (var work in queue.Reader.ReadAllAsync(lifetime.Token).ConfigureAwait(false))
                {
                    var levels = await work.Computation.ConfigureAwait(false);
                    using var lease = await budget.AcquireAsync(workerOwner, cancellationToken: lifetime.Token).ConfigureAwait(false);
                    lifetime.Token.ThrowIfCancellationRequested();
                    var segment = work.Segment;
                    waveform.Append(segment.Samples.AsSpan(checked((int)(segment.Start - segment.FirstSample)),
                        checked((int)(segment.End - segment.Start))));
                    AppendSpectrum(segment, levels.Span, buffers.CoarseLevels, lifetime.Token);
                }
            }
            catch
            {
                lifetime.Cancel();
                throw;
            }
        }
    }

    private async Task<ReadOnlyMemory<byte>> AnalyzeAsync(AudioAnalysisCacheSegment segment, AudioSpectrumSegmentProcessor processor,
        byte[] levels, AudioAnalysisWorkerBudget budget, CancellationToken token)
    {
        if (segment.Columns == 0)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
        using var lease = await budget.AcquireAsync(workerOwner, cancellationToken: token).ConfigureAwait(false);
        var count = checked(segment.Columns * recipe.FrequencyBins);
        await Task.Run(() => processor.AnalyzeInto(segment.Samples, segment.FirstSample,
            segment.FirstColumn * recipe.HopSize, segment.Columns, levels.AsSpan(0, count), token), token).ConfigureAwait(false);
        return levels.AsMemory(0, count);
    }

    private AudioAnalysisCacheSegment ReadSegment(AudioAnalysisPcmReader reader, AudioAnalysisPcmOverlap overlap,
        float[] samples, long cursor, long mediaStart, long mediaEnd, int segmentSamples, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        Array.Clear(samples);
        var first = checked(cursor - recipe.RawPadding);
        var start = Math.Max(first, mediaStart);
        var after = Math.Min(checked(first + samples.Length), Math.Min(mediaEnd, reader.EndSample ?? mediaEnd));
        if (overlap.HasSamples)
        {
            var overlapEnd = Math.Min(after, checked(overlap.FirstSample + overlap.Samples.Length));
            if (overlapEnd > start)
            {
                var count = checked((int)(overlapEnd - start));
                overlap.Samples.AsSpan(checked((int)(start - overlap.FirstSample)), count)
                    .CopyTo(samples.AsSpan(checked((int)(start - first)), count));
                start = overlapEnd;
            }
        }
        if (after > start)
        {
            reader.CopyTo(start, samples.AsSpan(checked((int)(start - first)), checked((int)(after - start))));
        }
        overlap.FirstSample = checked(first + samples.Length - overlap.Samples.Length);
        samples.AsSpan(samples.Length - overlap.Samples.Length).CopyTo(overlap.Samples);
        overlap.HasSamples = true;
        var coreStart = Math.Max(cursor, mediaStart);
        var coreEnd = Math.Max(coreStart, Math.Min(checked(cursor + segmentSamples), Math.Min(mediaEnd, reader.EndSample ?? mediaEnd)));
        var firstColumn = Ceiling(coreStart, recipe.HopSize * (long)recipe.Decimation);
        var afterColumn = Ceiling(coreEnd, recipe.HopSize * (long)recipe.Decimation);
        return new(first, coreStart, coreEnd, firstColumn, checked((int)(afterColumn - firstColumn)), samples);
    }

    private void AppendSpectrum(AudioAnalysisCacheSegment segment, ReadOnlySpan<byte> basis, byte[] coarseLevels, CancellationToken token)
    {
        if (segment.Columns == 0)
        {
            return;
        }
        store.AppendSpectrum(recipe.HopSize, segment.FirstColumn, segment.Columns, basis);
        for (var stride = recipe.HopSize * 2; stride <= MAXIMUM_SPECTRUM_STRIDE; stride *= 2)
        {
            token.ThrowIfCancellationRequested();
            var ratio = stride / recipe.HopSize;
            var firstColumn = Ceiling(segment.FirstColumn, ratio);
            var afterColumn = Ceiling(segment.FirstColumn + segment.Columns, ratio);
            var columns = checked((int)(afterColumn - firstColumn));
            if (columns == 0)
            {
                continue;
            }
            var levels = coarseLevels.AsSpan(0, checked(columns * recipe.FrequencyBins));
            for (var column = 0; column < columns; column++)
            {
                var sourceColumn = checked((int)((firstColumn + column) * ratio - segment.FirstColumn));
                for (var row = 0; row < recipe.FrequencyBins; row++)
                {
                    levels[row * columns + column] = basis[row * segment.Columns + sourceColumn];
                }
            }
            store.AppendSpectrum(stride, firstColumn, columns, levels);
        }
    }

    private void ApplyPendingExecution(AudioAnalysisWorkerBudget? ownedBudget)
    {
        lock (executionGate)
        {
            if (pendingExecution is null)
            {
                return;
            }
            execution = pendingExecution;
            pendingExecution = null;
        }
        ownedBudget?.UpdateMaximumWorkers(execution.MaximumWorkers);
    }

    private void UpdateCapacity(AudioAnalysisExecutionOptions value, int globalMaximum)
    {
        var (workers, bytes) = CalculateCapacity(value, globalMaximum);
        Volatile.Write(ref effectiveWorkers, workers);
        Interlocked.Exchange(ref plannedWorkingBytes, bytes);
    }

    private (int Workers, long Bytes) CalculateCapacity(AudioAnalysisExecutionOptions value, int globalMaximum)
    {
        var rawCount = checked(value.SegmentSamples + recipe.RawPadding * 2);
        var columns = checked(value.SegmentSamples / (recipe.HopSize * recipe.Decimation) + 1);
        var spectrumSamples = checked((long)(columns - 1) * recipe.HopSize + recipe.FftSize);
        var segmentBytes = checked((long)rawCount * sizeof(float) + spectrumSamples * sizeof(float) +
            (long)columns * recipe.FrequencyBins + 64L * recipe.FftSize + 16L * recipe.FrequencyBins + 8192);
        var fixedBytes = checked(512L * 1024 + (long)recipe.RawPadding * 2 * sizeof(float) +
            (long)((columns + 1) / 2) * recipe.FrequencyBins);
        var capacity = (value.WorkingBytes - fixedBytes) / segmentBytes;
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "分段内存超过分析工作预算。");
        }
        var workers = (int)Math.Min(capacity, Math.Min(value.EffectiveMaximumWorkers, globalMaximum));
        return (workers, checked(fixedBytes + workers * segmentBytes));
    }

    private void ValidateSegment(AudioAnalysisExecutionOptions value)
    {
        if (value.SegmentSamples % (recipe.HopSize * recipe.Decimation) != 0)
        {
            throw new ArgumentException("分析分段必须对齐媒体频谱步长。", nameof(value));
        }
    }

    private static long Ceiling(long value, long step)
    {
        return -AudioSpectrumWindowAnalyzer.Floor(-value, step) / step;
    }
}
