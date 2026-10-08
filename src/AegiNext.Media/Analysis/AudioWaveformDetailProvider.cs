using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

internal sealed class AudioWaveformDetailProvider : IAsyncDisposable
{
    private const int MAX_TILE_SAMPLES = 65536;
    private readonly Lock gate = new();
    private readonly Func<CancellationToken, IAudioSampleSource> sourceFactory;
    private readonly MediaTimelineMapping mapping;
    private readonly MediaTime duration;
    private readonly AudioAnalysisTileLruCache cache;
    private readonly AudioAnalysisWorkerBudget? budget;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim signal = new(0);
    private readonly Task worker;
    private readonly int tileSamples;
    private IAudioSampleSource? source;
    private AudioAnalysisPcmReader? reader;
    private long readThrough = long.MinValue;
    private long? sourceEnd;
    private AudioAnalysisWorkItem? pending;
    private AudioAnalysisWorkItem? active;
    private Task? closing;
    private long revision;
    private bool closed;

    internal AudioWaveformDetailProvider(Func<CancellationToken, IAudioSampleSource> sourceFactory,
        MediaTimelineMapping mapping, MediaTime duration, long maximumCachedBytes, AudioAnalysisWorkerBudget? budget = null)
    {
        this.sourceFactory = sourceFactory;
        this.mapping = mapping;
        this.duration = duration;
        this.budget = budget;
        cache = new(maximumCachedBytes);
        tileSamples = (int)Math.Clamp(maximumCachedBytes / sizeof(float), 1, MAX_TILE_SAMPLES);
        worker = Task.Run(RunAsync);
    }

    internal long CachedBytes
    {
        get
        {
            lock (gate)
            {
                return cache.Bytes;
            }
        }
    }

    internal void SetMaximumCachedBytes(long value)
    {
        lock (gate)
        {
            cache.SetMaximumBytes(value);
        }
    }

    internal Task<WaveformData> GetWaveformAsync(WaveformAnalysisRequest request, CancellationToken cancellationToken)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<WaveformData>(cancellationToken);
            }
            pending?.Cancel();
            active?.Cancel();
            pending = new(request, ++revision, cancellationToken);
            signal.Release();
            var registration = cancellationToken.UnsafeRegister(static state => ((AudioAnalysisWorkItem)state!).Cancel(), pending);
            return RequireWaveformAsync(pending.Completion.Task, registration);
        }
    }

    private static async Task<WaveformData> RequireWaveformAsync(Task<AudioAnalysisLayers> task, CancellationTokenRegistration registration)
    {
        using (registration)
        {
            return (await task.ConfigureAwait(false)).Waveform!;
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await signal.WaitAsync(lifetime.Token).ConfigureAwait(false);
                AudioAnalysisWorkItem work;
                lock (gate)
                {
                    if (pending is not { } next)
                    {
                        continue;
                    }
                    work = next;
                    pending = null;
                    active = work;
                }
                try
                {
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(work.Token, lifetime.Token);
                    using var lease = budget is null ? null : await budget.AcquireAsync(this, true, cancellation.Token).ConfigureAwait(false);
                    Check(work);
                    var waveform = ReadWaveform(work);
                    Check(work);
                    work.Completion.TrySetResult(new(waveform, null));
                }
                catch (OperationCanceledException)
                {
                    reader = null;
                    readThrough = long.MinValue;
                    work.Cancel();
                }
                catch (Exception error)
                {
                    reader = null;
                    readThrough = long.MinValue;
                    work.Completion.TrySetException(error);
                }
                finally
                {
                    lock (gate)
                    {
                        active = null;
                    }
                }
            }
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested)
        {
        }
        finally
        {
            source?.Dispose();
        }
    }

    private WaveformData ReadWaveform(AudioAnalysisWorkItem work)
    {
        var request = work.Request;
        var mediaStart = mapping.Origin.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        var mediaEnd = mapping.ToMediaTime(duration).ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        var first = request.Start.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value + mediaStart;
        var peaks = new float[request.BucketCount * 2];
        for (var bucket = 0; bucket < request.BucketCount; bucket++)
        {
            Check(work);
            var start = first + (long)bucket * request.SamplesPerBucket;
            var end = Math.Min(start + request.SamplesPerBucket, Math.Min(mediaEnd, sourceEnd ?? mediaEnd));
            while (start < end)
            {
                Check(work);
                var index = AudioSpectrumWindowAnalyzer.Floor(start, tileSamples) / tileSamples;
                var tile = ReadTile(index, mediaStart, mediaEnd, work);
                var offset = checked((int)(start - index * tileSamples));
                var count = (int)Math.Min(tile.Samples.Length - offset, end - start);
                var (minimum, maximum) = AudioWaveformPeakReducer.Reduce(tile.Samples.Span.Slice(offset, count));
                peaks[bucket * 2] = Math.Min(peaks[bucket * 2], minimum);
                peaks[bucket * 2 + 1] = Math.Max(peaks[bucket * 2 + 1], maximum);
                start += count;
            }
        }
        return new(request, peaks);
    }

    private AudioAnalysisTile ReadTile(long index, long mediaStart, long mediaEnd, AudioAnalysisWorkItem work)
    {
        var key = new AudioAnalysisTileKey(AudioAnalysisTileKind.PCM, tileSamples, index);
        lock (gate)
        {
            if (cache.Find(key) is { } cached)
            {
                return cached;
            }
        }
        var first = index * tileSamples;
        var samples = new float[tileSamples];
        var start = Math.Max(first, mediaStart);
        var end = Math.Min(first + tileSamples, Math.Min(mediaEnd, sourceEnd ?? mediaEnd));
        if (start < end)
        {
            if (source is null)
            {
                var opened = sourceFactory(lifetime.Token);
                lock (gate)
                {
                    source = opened;
                    if (closed)
                    {
                        source.Cancel();
                    }
                }
                if (source.Format.SampleRate != WaveformAnalyzer.SAMPLE_RATE || source.Format.Channels != 1)
                {
                    throw new InvalidDataException("细波形分析要求48 kHz单声道源。");
                }
            }
            Check(work);
            if (reader is null || readThrough != start)
            {
                source.Seek(new(start, WaveformAnalyzer.SAMPLE_RATE), lifetime.Token);
                reader = new(source, CheckActive, lifetime.Token);
            }
            reader.CopyTo(start, samples.AsSpan((int)(start - first), (int)(end - start)));
            if (reader.EndSample is { } eof)
            {
                sourceEnd = Math.Min(eof, sourceEnd ?? eof);
            }
            readThrough = end;
        }
        Check(work);
        var tile = new AudioAnalysisTile(key, samples);
        lock (gate)
        {
            cache.Add(tile);
        }
        return tile;
    }

    private void Check(AudioAnalysisWorkItem work)
    {
        lifetime.Token.ThrowIfCancellationRequested();
        work.Token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (closed || work.Revision != revision || work.Completion.Task.IsCompleted)
            {
                throw new OperationCanceledException();
            }
        }
    }

    private void CheckActive()
    {
        AudioAnalysisWorkItem work;
        lock (gate)
        {
            work = active ?? throw new OperationCanceledException();
        }
        Check(work);
    }

    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (closing is null)
            {
                closed = true;
                pending?.Cancel();
                active?.Cancel();
                source?.Cancel();
                lifetime.Cancel();
                closing = CloseAsync();
            }
            return new(closing);
        }
    }

    private async Task CloseAsync()
    {
        await worker.ConfigureAwait(false);
        lock (gate)
        {
            cache.Clear();
        }
        signal.Dispose();
        lifetime.Dispose();
    }
}
