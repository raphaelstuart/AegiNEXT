using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

/// <summary>持有单个 PCM 解码器，以固定时间网格串行分析窗口并保存有界分辨率 tile。</summary>
public sealed class AudioAnalysisSession : IAsyncDisposable
{
    private const int TILE_COLUMNS = 1024;
    private const long DEFAULT_CACHE_BYTES = 64L * 1024 * 1024;
    private readonly Lock gate = new();
    private readonly Func<CancellationToken, IAudioSampleSource> sourceFactory;
    private readonly MediaTimelineMapping mapping;
    private readonly AudioAnalysisTileCache cache;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim signal = new(0);
    private readonly Task worker;
    private IAudioSampleSource? source;
    private AudioAnalysisWorkItem? foreground;
    private AudioAnalysisWorkItem? overview;
    private AudioAnalysisWorkItem? active;
    private Task? closing;
    private long revision;
    private bool closed;

    /// <summary>由会话工作线程打开并独占 48 kHz 单声道源；关闭会话后才取消与释放解码器。</summary>
    public AudioAnalysisSession(Func<CancellationToken, IAudioSampleSource> sourceFactory, MediaTimelineMapping mapping,
        MediaTime duration, long maximumCachedBytes = DEFAULT_CACHE_BYTES)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, MediaTime.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCachedBytes);
        this.sourceFactory = sourceFactory;
        this.mapping = mapping;
        Duration = duration;
        cache = new(maximumCachedBytes);
        worker = Task.Run(RunAsync);
    }

    public MediaTime Duration { get; }

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

    /// <summary>创建独立于播放的分析会话，工程零点由媒体映射确定。</summary>
    public static AudioAnalysisSession Open(string path, int streamIndex, MediaTimelineMapping mapping, MediaTime duration)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
        return new(token => FfmpegAudioDecoder.Open(path, streamIndex, new(WaveformAnalyzer.SAMPLE_RATE, 1), token), mapping, duration);
    }

    /// <summary>请求最新视口窗口；替换请求立即结束旧任务，但不取消共享解码器。</summary>
    public Task<AudioAnalysisWindow> GetWindowAsync(WaveformAnalysisRequest request, bool includeSpectrum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            foreground?.Cancel();
            if (active is { IsOverview: false })
            {
                active.Cancel();
            }
            foreground = new(request, includeSpectrum, ++revision, false, cancellationToken);
            signal.Release();
            return foreground.Completion.Task;
        }
    }

    /// <summary>提交低优先级全片概览；前台视口请求可在 PCM 块或 FFT 边界抢占，已完成 tile 可复用。</summary>
    public Task<AudioAnalysisWindow> GetOverviewAsync(WaveformAnalysisRequest request, bool includeSpectrum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            overview?.Cancel();
            if (active is { IsOverview: true })
            {
                active.Cancel();
            }
            overview = new(request, includeSpectrum, revision, true, cancellationToken);
            signal.Release();
            return overview.Completion.Task;
        }
    }

    private async Task RunAsync()
    {
        try
        {
            while (true)
            {
                await signal.WaitAsync(lifetime.Token).ConfigureAwait(false);
                AudioAnalysisWorkItem? work;
                lock (gate)
                {
                    work = foreground ?? overview;
                    if (work is null)
                    {
                        continue;
                    }
                    if (ReferenceEquals(work, foreground))
                    {
                        foreground = null;
                    }
                    else
                    {
                        overview = null;
                    }
                    active = work;
                }
                try
                {
                    Check(work);
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
                    }
                    var waveform = ReadWaveform(work);
                    var spectrum = work.Spectrum ? ReadSpectrum(work) : null;
                    Check(work);
                    work.Completion.TrySetResult(new(waveform, spectrum));
                }
                catch (OperationCanceledException)
                {
                    lock (gate)
                    {
                        if (work.IsOverview && !closed && !work.Token.IsCancellationRequested &&
                            !work.Completion.Task.IsCompleted && overview is null)
                        {
                            overview = work;
                        }
                        else
                        {
                            work.Cancel();
                        }
                    }
                }
                catch (Exception error)
                {
                    work.Completion.TrySetException(error);
                }
                finally
                {
                    lock (gate)
                    {
                        active = null;
                        if (!closed && (foreground is not null || overview is not null))
                        {
                            signal.Release();
                        }
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

    private void Check(AudioAnalysisWorkItem work)
    {
        lifetime.Token.ThrowIfCancellationRequested();
        work.Token.ThrowIfCancellationRequested();
        lock (gate)
        {
            if (closed || work.Completion.Task.IsCompleted ||
                (work.IsOverview ? foreground is not null : work.Revision != revision))
            {
                throw new OperationCanceledException();
            }
        }
    }

    private WaveformData ReadWaveform(AudioAnalysisWorkItem work)
    {
        var request = work.Request;
        var first = request.Start.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value / request.SamplesPerBucket;
        var peaks = new float[request.BucketCount * 2];
        var copied = 0;
        while (copied < request.BucketCount)
        {
            Check(work);
            var column = first + copied;
            var tileIndex = column / TILE_COLUMNS;
            var key = new AudioAnalysisTileKey(false, request.SamplesPerBucket, tileIndex);
            var tile = Find(key);
            if (tile is null)
            {
                var tileRequest = new WaveformAnalysisRequest(new(tileIndex * TILE_COLUMNS * request.SamplesPerBucket,
                    WaveformAnalyzer.SAMPLE_RATE), request.SamplesPerBucket, TILE_COLUMNS);
                source!.Seek(mapping.ToMediaTime(tileRequest.Start), lifetime.Token);
                Check(work);
                var data = WaveformAnalyzer.Analyze(source, mapping.Origin, tileRequest, () => Check(work), lifetime.Token);
                tile = new(key, data, null);
                Save(tile, work);
            }
            var offset = (int)(column % TILE_COLUMNS);
            var count = Math.Min(TILE_COLUMNS - offset, request.BucketCount - copied);
            tile.Waveform!.Peaks.Span.Slice(offset * 2, count * 2).CopyTo(peaks.AsSpan(copied * 2));
            copied += count;
        }
        return new(request, peaks);
    }

    private SpectrogramData ReadSpectrum(AudioAnalysisWorkItem work)
    {
        var stride = SpectrogramAnalyzer.HOP_SIZE;
        while ((long)stride * 2 <= work.Request.SamplesPerBucket / 3)
        {
            stride = checked(stride * 2);
        }
        var half = stride / 2;
        var start = mapping.ToMediaTime(work.Request.Start).ToTimestamp(new(1, SpectrogramAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value;
        var end = mapping.ToMediaTime(work.Request.End).ToTimestamp(new(1, SpectrogramAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        var first = AudioSpectrumWindowAnalyzer.Floor(start + half, stride) / stride;
        var after = AudioSpectrumWindowAnalyzer.Floor(end + half - 1, stride) / stride + 1;
        var count = checked((int)(after - first));
        var levels = new byte[checked(count * SpectrogramAnalyzer.FREQUENCY_BINS)];
        var copied = 0;
        while (copied < count)
        {
            Check(work);
            var column = first + copied;
            var tileIndex = AudioSpectrumWindowAnalyzer.Floor(column, TILE_COLUMNS) / TILE_COLUMNS;
            var key = new AudioAnalysisTileKey(true, stride, tileIndex);
            var tile = Find(key);
            if (tile is null)
            {
                var data = AudioSpectrumWindowAnalyzer.Analyze(source!, mapping.Origin, tileIndex * TILE_COLUMNS * stride,
                    stride, TILE_COLUMNS, () => Check(work), lifetime.Token);
                tile = new(key, null, data);
                Save(tile, work);
            }
            var offset = (int)(column - tileIndex * TILE_COLUMNS);
            var columns = Math.Min(TILE_COLUMNS - offset, count - copied);
            for (var row = 0; row < SpectrogramAnalyzer.FREQUENCY_BINS; row++)
            {
                tile.Spectrogram!.Levels.Span.Slice(row * TILE_COLUMNS + offset, columns).CopyTo(levels.AsSpan(row * count + copied));
            }
            copied += columns;
        }
        return new(count, SpectrogramAnalyzer.FREQUENCY_BINS,
            mapping.ToProjectTime(new(first * stride - half, SpectrogramAnalyzer.SAMPLE_RATE)),
            new(stride, SpectrogramAnalyzer.SAMPLE_RATE), levels);
    }

    private AudioAnalysisTile? Find(AudioAnalysisTileKey key)
    {
        lock (gate)
        {
            return cache.Find(key);
        }
    }

    private void Save(AudioAnalysisTile tile, AudioAnalysisWorkItem work)
    {
        Check(work);
        lock (gate)
        {
            cache.Add(tile);
        }
    }

    /// <summary>终止请求、取消阻塞解码并等待工作线程退出后释放会话资源。</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (closing is null)
            {
                closed = true;
                foreground?.Cancel();
                overview?.Cancel();
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
