using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

/// <summary>持有单个 PCM 解码器，以固定时间网格串行分析窗口并保存有界分辨率 tile。</summary>
public sealed class AudioAnalysisSession : IAsyncDisposable
{
    private const int TILE_COLUMNS = 1024;
    private const int BASE_WAVEFORM_SAMPLES = 512;
    private const int PREVIEW_WINDOW_SAMPLES = 1024;
    private const long DEFAULT_CACHE_BYTES = 64L * 1024 * 1024;
    private readonly Lock gate = new();
    private readonly Func<CancellationToken, IAudioSampleSource> sourceFactory;
    private readonly MediaTimelineMapping mapping;
    private readonly AudioAnalysisTileCache cache;
    private readonly CancellationTokenSource lifetime = new();
    private readonly SemaphoreSlim signal = new(0);
    private readonly Task worker;
    private IAudioSampleSource? source;
    private AudioAnalysisPcmReader? pcmReader;
    private AudioAnalysisTile? lastPcmTile;
    private long pcmReadThrough;
    private long? confirmedSourceEnd;
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
        return RequireWaveformAsync(GetLayersAsync(request, true, includeSpectrum, cancellationToken));
    }

    /// <summary>仅请求可见层；普通替换在 PCM 块及 FFT 边界停止旧请求，不终止共享源。</summary>
    public Task<AudioAnalysisLayers> GetLayersAsync(WaveformAnalysisRequest request, bool includeWaveform, bool includeSpectrum,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<AudioAnalysisLayers>(cancellationToken);
            }
            foreground?.Cancel();
            if (active is { IsOverview: false })
            {
                active.Cancel();
            }
            foreground = new(request, includeSpectrum, ++revision, false, cancellationToken, includeWaveform);
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
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<AudioAnalysisWindow>(cancellationToken);
            }
            overview?.Cancel();
            if (active is { IsOverview: true })
            {
                active.Cancel();
            }
            overview = new(request, includeSpectrum, revision, true, cancellationToken);
            signal.Release();
            return RequireWaveformAsync(overview.Completion.Task);
        }
    }

    private static async Task<AudioAnalysisWindow> RequireWaveformAsync(Task<AudioAnalysisLayers> pending)
    {
        var result = await pending.ConfigureAwait(false);
        return new(result.Waveform ?? throw new InvalidOperationException("请求的波形层未返回。"), result.Spectrogram);
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
                    var result = ReadLayers(work);
                    Check(work);
                    work.Completion.TrySetResult(result);
                }
                catch (OperationCanceledException)
                {
                    ResetPcmCursor();
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
                    ResetPcmCursor();
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

    private AudioAnalysisLayers ReadLayers(AudioAnalysisWorkItem work)
    {
        var request = work.Request;
        var tileSamples = request.Mode == AudioAnalysisMode.PREVIEW
            ? AudioAnalysisSampleReader.PREVIEW_TILE_SAMPLES : AudioAnalysisSampleReader.TILE_SAMPLES;
        var pcmTimeBase = new MediaTimeBase(1, WaveformAnalyzer.SAMPLE_RATE);
        var mediaStart = mapping.Origin.ToTimestamp(pcmTimeBase, MediaTimeRounding.CEILING).Value;
        var mediaEnd = mapping.ToMediaTime(Duration).ToTimestamp(pcmTimeBase, MediaTimeRounding.CEILING).Value;
        mediaEnd = Math.Min(mediaEnd, confirmedSourceEnd ?? mediaEnd);
        var projectStart = request.Start.ToTimestamp(pcmTimeBase, MediaTimeRounding.FLOOR).Value;
        var projectEnd = (request.End < Duration ? request.End : Duration).ToTimestamp(pcmTimeBase, MediaTimeRounding.CEILING).Value;
        projectEnd = Math.Min(projectEnd, Math.Max(0, mediaEnd - mediaStart));
        var waveformResolution = request.Mode == AudioAnalysisMode.PREVIEW ? request.SamplesPerBucket
            : Math.Min(request.SamplesPerBucket, AudioAnalysisSampleReader.TILE_SAMPLES);
        var waveformTileColumns = Math.Max(1, Math.Min(TILE_COLUMNS, AudioAnalysisSampleReader.TILE_SAMPLES / waveformResolution));
        var waveformColumn = projectStart / waveformResolution;
        var waveformAfter = work.Waveform ? Ceiling(projectEnd, waveformResolution) : waveformColumn;
        var peaks = work.Waveform ? new float[request.BucketCount * 2] : null;
        var stride = SpectrogramAnalyzer.HOP_SIZE;
        while ((long)stride * 2 <= request.SamplesPerBucket / 3)
        {
            stride = checked(stride * 2);
        }
        var half = stride / 2;
        var start = mapping.ToMediaTime(request.Start).ToTimestamp(new(1, SpectrogramAnalyzer.SAMPLE_RATE), MediaTimeRounding.FLOOR).Value;
        var end = mapping.ToMediaTime(request.End).ToTimestamp(new(1, SpectrogramAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        var first = AudioSpectrumWindowAnalyzer.Floor(start + half, stride) / stride;
        var after = AudioSpectrumWindowAnalyzer.Floor(end + half - 1, stride) / stride + 1;
        var count = checked((int)(after - first));
        var levels = work.Spectrum ? new byte[checked(count * SpectrogramAnalyzer.FREQUENCY_BINS)] : null;
        var spectrumColumn = Math.Max(first, Ceiling(mediaStart, stride * 3L));
        var spectrumAfter = work.Spectrum ? Math.Min(after, Ceiling(mediaEnd, stride * 3L)) : spectrumColumn;
        var reader = new AudioAnalysisSampleReader(index => ReadPcmTile(index, tileSamples, mediaStart, mediaEnd, work),
            mediaStart, mediaEnd, tileSamples);
        while (waveformColumn < waveformAfter || spectrumColumn < spectrumAfter)
        {
            Check(work);
            var waveformTileIndex = waveformColumn / waveformTileColumns;
            var spectrumTileIndex = AudioSpectrumWindowAnalyzer.Floor(spectrumColumn * stride * 3,
                tileSamples) / tileSamples;
            var waveformTileStart = waveformTileIndex * waveformTileColumns * waveformResolution + mediaStart;
            var spectrumTileStart = spectrumTileIndex * tileSamples;
            if (waveformColumn < waveformAfter && (spectrumColumn >= spectrumAfter || waveformTileStart <= spectrumTileStart))
            {
                var tile = ReadWaveformTile(new(AudioAnalysisTileKind.WAVEFORM, waveformResolution, waveformTileIndex, request.Mode),
                    waveformTileColumns, mediaStart, reader, work).Waveform!;
                var tileFirst = waveformTileIndex * waveformTileColumns;
                var columns = (int)Math.Min(tileFirst + waveformTileColumns - waveformColumn, waveformAfter - waveformColumn);
                var offset = (int)(waveformColumn - tileFirst);
                for (var column = 0; column < columns; column++)
                {
                    var bucket = (int)(((waveformColumn + column) * waveformResolution - projectStart) / request.SamplesPerBucket);
                    peaks![bucket * 2] = Math.Min(peaks[bucket * 2], tile.Peaks.Span[(offset + column) * 2]);
                    peaks[bucket * 2 + 1] = Math.Max(peaks[bucket * 2 + 1], tile.Peaks.Span[(offset + column) * 2 + 1]);
                }
                waveformColumn += columns;
            }
            else
            {
                var tileFirst = Ceiling(spectrumTileStart, stride * 3L);
                var tileAfter = Ceiling(spectrumTileStart + tileSamples, stride * 3L);
                var tileColumns = checked((int)(tileAfter - tileFirst));
                var tile = ReadSpectrumTile(new(AudioAnalysisTileKind.SPECTRUM, stride, spectrumTileIndex, request.Mode), tileFirst,
                    tileColumns, tileSamples, mediaStart, Math.Min(mediaEnd, confirmedSourceEnd ?? mediaEnd), reader, work).Spectrogram!;
                var offset = (int)(spectrumColumn - tileFirst);
                spectrumAfter = Math.Min(spectrumAfter, Ceiling(confirmedSourceEnd ?? mediaEnd, stride * 3L));
                var columns = (int)Math.Min(tileAfter - spectrumColumn, spectrumAfter - spectrumColumn);
                columns = Math.Max(0, columns);
                for (var row = 0; row < SpectrogramAnalyzer.FREQUENCY_BINS; row++)
                {
                    tile.Levels.Span.Slice(row * tileColumns + offset, columns)
                        .CopyTo(levels!.AsSpan(row * count + (int)(spectrumColumn - first), columns));
                }
                spectrumColumn += columns;
            }
            if (confirmedSourceEnd is { } sourceEnd)
            {
                waveformAfter = Math.Min(waveformAfter, Ceiling(Math.Max(0, sourceEnd - mediaStart), waveformResolution));
                spectrumAfter = Math.Min(spectrumAfter, Ceiling(sourceEnd, stride * 3L));
            }
        }
        return new(peaks is null ? null : new(request, peaks), levels is null ? null :
            new(count, SpectrogramAnalyzer.FREQUENCY_BINS,
                mapping.ToProjectTime(new(first * stride - half, SpectrogramAnalyzer.SAMPLE_RATE)),
                new(stride, SpectrogramAnalyzer.SAMPLE_RATE), levels));
    }

    private AudioAnalysisTile ReadWaveformTile(AudioAnalysisTileKey key, int columns, long mediaStart,
        AudioAnalysisSampleReader reader, AudioAnalysisWorkItem work)
    {
        if (Find(key) is { } cached)
        {
            return cached;
        }
        if (key.Mode == AudioAnalysisMode.EXACT && key.SamplesPerColumn > BASE_WAVEFORM_SAMPLES)
        {
            var basis = ReadWaveformTile(key with { SamplesPerColumn = BASE_WAVEFORM_SAMPLES },
                AudioAnalysisSampleReader.TILE_SAMPLES / BASE_WAVEFORM_SAMPLES, mediaStart, reader, work);
            return AggregateWaveformTile(key, columns, basis.Waveform!, work);
        }
        if (key.Mode == AudioAnalysisMode.PREVIEW && key.SamplesPerColumn >= BASE_WAVEFORM_SAMPLES)
        {
            for (var resolution = key.SamplesPerColumn / 2; resolution >= BASE_WAVEFORM_SAMPLES; resolution /= 2)
            {
                if (TryAggregatePreviewWaveformTile(key, columns, resolution, work) is { } reused)
                {
                    return reused;
                }
            }
            for (var resolution = (long)key.SamplesPerColumn * 2; resolution <= 1L << 30; resolution *= 2)
            {
                if (TryResamplePreviewWaveformTile(key, columns, (int)resolution, work) is { } reused)
                {
                    return reused;
                }
            }
        }
        var first = key.Index * columns * key.SamplesPerColumn;
        var peaks = new float[columns * 2];
        var end = Math.Min(first + (long)columns * key.SamplesPerColumn,
            Duration.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value);
        if (confirmedSourceEnd is { } sourceEnd)
        {
            end = Math.Min(end, Math.Max(0, sourceEnd - mediaStart));
        }
        for (var column = 0; column < columns; column++)
        {
            var start = first + (long)column * key.SamplesPerColumn;
            var after = Math.Min(start + key.SamplesPerColumn, end);
            if (key.Mode == AudioAnalysisMode.PREVIEW)
            {
                var center = start + (after - start) / 2;
                start = Math.Max(start, center - PREVIEW_WINDOW_SAMPLES / 2);
                after = Math.Min(after, center + PREVIEW_WINDOW_SAMPLES / 2);
            }
            while (start < after)
            {
                Check(work);
                var samples = reader.ReadSpan(mediaStart + start, (int)Math.Min(1024, after - start));
                var (minimum, maximum) = AudioWaveformPeakReducer.Reduce(samples);
                peaks[column * 2] = Math.Min(peaks[column * 2], minimum);
                peaks[column * 2 + 1] = Math.Max(peaks[column * 2 + 1], maximum);
                start += samples.Length;
            }
        }
        return SaveWaveformTile(key, columns, peaks, work);
    }

    private AudioAnalysisTile? TryResamplePreviewWaveformTile(AudioAnalysisTileKey key, int columns, int resolution,
        AudioAnalysisWorkItem work)
    {
        var basisColumns = Math.Max(1, Math.Min(TILE_COLUMNS, AudioAnalysisSampleReader.TILE_SAMPLES / resolution));
        var first = key.Index * columns * key.SamplesPerColumn;
        var end = Duration.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        var peaks = new float[columns * 2];
        WaveformData? basis = null;
        var previousIndex = -1L;
        for (var column = 0; column < columns; column++)
        {
            Check(work);
            var sample = first + (long)column * key.SamplesPerColumn;
            if (sample >= end)
            {
                break;
            }
            var basisColumn = sample / resolution;
            var tileIndex = basisColumn / basisColumns;
            if (tileIndex != previousIndex)
            {
                basis = Find(key with { SamplesPerColumn = resolution, Index = tileIndex })?.Waveform;
                previousIndex = tileIndex;
                if (basis is null)
                {
                    return null;
                }
            }
            var offset = checked((int)(basisColumn % basisColumns)) * 2;
            peaks[column * 2] = basis!.Peaks.Span[offset];
            peaks[column * 2 + 1] = basis.Peaks.Span[offset + 1];
        }
        return SaveWaveformTile(key, columns, peaks, work);
    }

    private AudioAnalysisTile? TryAggregatePreviewWaveformTile(AudioAnalysisTileKey key, int columns, int resolution,
        AudioAnalysisWorkItem work)
    {
        var basisColumns = Math.Max(1, Math.Min(TILE_COLUMNS, AudioAnalysisSampleReader.TILE_SAMPLES / resolution));
        var first = key.Index * columns * key.SamplesPerColumn / resolution;
        var ratio = key.SamplesPerColumn / resolution;
        var end = Duration.ToTimestamp(new(1, WaveformAnalyzer.SAMPLE_RATE), MediaTimeRounding.CEILING).Value;
        var peaks = new float[columns * 2];
        WaveformData? basis = null;
        var previousIndex = -1L;
        for (var column = 0; column < columns; column++)
        {
            Check(work);
            for (var index = 0; index < ratio; index++)
            {
                if (index % TILE_COLUMNS == 0)
                {
                    Check(work);
                }
                var basisColumn = first + (long)column * ratio + index;
                if (basisColumn * resolution >= end)
                {
                    break;
                }
                var tileIndex = basisColumn / basisColumns;
                if (tileIndex != previousIndex)
                {
                    var basisKey = key with { SamplesPerColumn = resolution, Index = tileIndex };
                    basis = (Find(basisKey) ?? Find(basisKey with { Mode = AudioAnalysisMode.EXACT }))?.Waveform;
                    previousIndex = tileIndex;
                    if (basis is null)
                    {
                        return null;
                    }
                }
                var offset = checked((int)(basisColumn % basisColumns)) * 2;
                peaks[column * 2] = Math.Min(peaks[column * 2], basis!.Peaks.Span[offset]);
                peaks[column * 2 + 1] = Math.Max(peaks[column * 2 + 1], basis.Peaks.Span[offset + 1]);
            }
        }
        return SaveWaveformTile(key, columns, peaks, work);
    }

    private AudioAnalysisTile AggregateWaveformTile(AudioAnalysisTileKey key, int columns, WaveformData basis,
        AudioAnalysisWorkItem work)
    {
        var peaks = new float[columns * 2];
        var ratio = key.SamplesPerColumn / basis.SamplesPerBucket;
        for (var column = 0; column < columns; column++)
        {
            Check(work);
            for (var index = column * ratio; index < (column + 1) * ratio; index++)
            {
                peaks[column * 2] = Math.Min(peaks[column * 2], basis.Peaks.Span[index * 2]);
                peaks[column * 2 + 1] = Math.Max(peaks[column * 2 + 1], basis.Peaks.Span[index * 2 + 1]);
            }
        }
        return SaveWaveformTile(key, columns, peaks, work);
    }

    private AudioAnalysisTile SaveWaveformTile(AudioAnalysisTileKey key, int columns, float[] peaks, AudioAnalysisWorkItem work)
    {
        var first = key.Index * columns * key.SamplesPerColumn;
        var data = new WaveformData(new(new(first, WaveformAnalyzer.SAMPLE_RATE), key.SamplesPerColumn, columns, key.Mode), peaks);
        var tile = new AudioAnalysisTile(key, data, null);
        Save(tile, work);
        return tile;
    }

    private AudioAnalysisTile ReadSpectrumTile(AudioAnalysisTileKey key, long firstColumn, int columns,
        int tileSamples, long mediaStart, long mediaEnd, AudioAnalysisSampleReader reader, AudioAnalysisWorkItem work)
    {
        if (Find(key) is { } cached)
        {
            return cached;
        }
        for (var resolution = key.SamplesPerColumn / 2; resolution >= SpectrogramAnalyzer.HOP_SIZE; resolution /= 2)
        {
            if (Find(key with { SamplesPerColumn = resolution })?.Spectrogram is not { } basis)
            {
                continue;
            }
            var levels = new byte[columns * SpectrogramAnalyzer.FREQUENCY_BINS];
            var firstBasis = Ceiling(key.Index * tileSamples, resolution * 3L);
            for (var column = 0; column < columns; column++)
            {
                Check(work);
                var sourceColumn = checked((int)((firstColumn + column) * key.SamplesPerColumn / resolution - firstBasis));
                for (var row = 0; row < SpectrogramAnalyzer.FREQUENCY_BINS; row++)
                {
                    levels[row * columns + column] = basis.Levels.Span[row * basis.Width + sourceColumn];
                }
            }
            return SaveSpectrumTile(key, firstColumn, columns, levels, work);
        }
        if (key.Mode == AudioAnalysisMode.PREVIEW)
        {
            for (var resolution = key.SamplesPerColumn; resolution >= SpectrogramAnalyzer.HOP_SIZE; resolution /= 2)
            {
                if (TryReuseExactSpectrumTile(key, firstColumn, columns, resolution, work) is { } reused)
                {
                    return reused;
                }
            }
            for (var resolution = (long)key.SamplesPerColumn * 2; resolution <= 1L << 30; resolution *= 2)
            {
                if (TryResamplePreviewSpectrumTile(key, firstColumn, columns, (int)resolution,
                        tileSamples, mediaStart, mediaEnd, work) is { } reused)
                {
                    return reused;
                }
            }
        }
        var data = AudioSpectrumWindowAnalyzer.Analyze(reader.Read, reader.Prepare, mapping.Origin,
            firstColumn * key.SamplesPerColumn, key.SamplesPerColumn, columns, mediaStart,
            () => Math.Min(mediaEnd, confirmedSourceEnd ?? mediaEnd), () => Check(work));
        if (key.Mode == AudioAnalysisMode.EXACT && !work.Waveform &&
            work.Request.SamplesPerBucket <= AudioAnalysisSampleReader.TILE_SAMPLES)
        {
            var projectTile = Math.Max(0, AudioSpectrumWindowAnalyzer.Floor(key.Index * tileSamples - mediaStart,
                AudioAnalysisSampleReader.TILE_SAMPLES) / AudioAnalysisSampleReader.TILE_SAMPLES);
            ReadWaveformTile(new(AudioAnalysisTileKind.WAVEFORM, BASE_WAVEFORM_SAMPLES, projectTile),
                AudioAnalysisSampleReader.TILE_SAMPLES / BASE_WAVEFORM_SAMPLES, mediaStart, reader, work);
        }
        var tile = new AudioAnalysisTile(key, null, data);
        Save(tile, work);
        return tile;
    }

    private AudioAnalysisTile? TryReuseExactSpectrumTile(AudioAnalysisTileKey key, long firstColumn, int columns,
        int resolution, AudioAnalysisWorkItem work)
    {
        var levels = new byte[columns * SpectrogramAnalyzer.FREQUENCY_BINS];
        SpectrogramData? basis = null;
        var previousIndex = long.MinValue;
        for (var column = 0; column < columns; column++)
        {
            Check(work);
            var center = (firstColumn + column) * key.SamplesPerColumn;
            var tileIndex = AudioSpectrumWindowAnalyzer.Floor(center * 3, AudioAnalysisSampleReader.TILE_SAMPLES)
                            / AudioAnalysisSampleReader.TILE_SAMPLES;
            if (tileIndex != previousIndex)
            {
                basis = Find(key with { SamplesPerColumn = resolution, Index = tileIndex, Mode = AudioAnalysisMode.EXACT })?.Spectrogram;
                previousIndex = tileIndex;
                if (basis is null)
                {
                    return null;
                }
            }
            var offset = checked((int)(center / resolution
                - Ceiling(tileIndex * AudioAnalysisSampleReader.TILE_SAMPLES, resolution * 3L)));
            for (var row = 0; row < SpectrogramAnalyzer.FREQUENCY_BINS; row++)
            {
                levels[row * columns + column] = basis!.Levels.Span[row * basis.Width + offset];
            }
        }
        return SaveSpectrumTile(key, firstColumn, columns, levels, work);
    }

    private AudioAnalysisTile? TryResamplePreviewSpectrumTile(AudioAnalysisTileKey key, long firstColumn, int columns,
        int resolution, int tileSamples, long mediaStart, long mediaEnd, AudioAnalysisWorkItem work)
    {
        var levels = new byte[columns * SpectrogramAnalyzer.FREQUENCY_BINS];
        var firstBasis = Ceiling(mediaStart, resolution * 3L);
        var afterBasis = Ceiling(mediaEnd, resolution * 3L);
        if (firstBasis >= afterBasis)
        {
            return null;
        }
        SpectrogramData? basis = null;
        var previousIndex = long.MinValue;
        for (var column = 0; column < columns; column++)
        {
            Check(work);
            var center = (firstColumn + column) * key.SamplesPerColumn;
            if (center * 3 < mediaStart || center * 3 >= mediaEnd)
            {
                continue;
            }
            var basisColumn = Math.Clamp(AudioSpectrumWindowAnalyzer.Floor(center + resolution / 2, resolution) / resolution,
                firstBasis, afterBasis - 1);
            var tileIndex = AudioSpectrumWindowAnalyzer.Floor(basisColumn * resolution * 3, tileSamples) / tileSamples;
            if (tileIndex != previousIndex)
            {
                basis = Find(key with { SamplesPerColumn = resolution, Index = tileIndex })?.Spectrogram;
                previousIndex = tileIndex;
                if (basis is null)
                {
                    return null;
                }
            }
            var offset = checked((int)(basisColumn - Ceiling(tileIndex * tileSamples, resolution * 3L)));
            for (var row = 0; row < SpectrogramAnalyzer.FREQUENCY_BINS; row++)
            {
                levels[row * columns + column] = basis!.Levels.Span[row * basis.Width + offset];
            }
        }
        return SaveSpectrumTile(key, firstColumn, columns, levels, work);
    }

    private AudioAnalysisTile SaveSpectrumTile(AudioAnalysisTileKey key, long firstColumn, int columns,
        byte[] levels, AudioAnalysisWorkItem work)
    {
        var data = new SpectrogramData(columns, SpectrogramAnalyzer.FREQUENCY_BINS,
            mapping.ToProjectTime(new(firstColumn * key.SamplesPerColumn - key.SamplesPerColumn / 2,
                SpectrogramAnalyzer.SAMPLE_RATE)), new(key.SamplesPerColumn, SpectrogramAnalyzer.SAMPLE_RATE), levels);
        var tile = new AudioAnalysisTile(key, null, data);
        Save(tile, work);
        return tile;
    }

    private AudioAnalysisTile ReadPcmTile(long index, int tileSamples, long mediaStart, long mediaEnd, AudioAnalysisWorkItem work)
    {
        var key = new AudioAnalysisTileKey(AudioAnalysisTileKind.PCM, tileSamples, index, work.Request.Mode);
        if (Find(key) is { } cached)
        {
            return cached;
        }
        Check(work);
        var first = index * tileSamples - AudioAnalysisSampleReader.PADDING;
        var samples = new float[tileSamples + AudioAnalysisSampleReader.PADDING * 2];
        var start = Math.Max(first, mediaStart);
        var end = Math.Min(first + samples.Length, Math.Min(mediaEnd, confirmedSourceEnd ?? mediaEnd));
        if (start >= end)
        {
            var silent = new AudioAnalysisTile(key, null, null, samples);
            Save(silent, work);
            return silent;
        }
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
        Check(work);
        if (source.Format.SampleRate != WaveformAnalyzer.SAMPLE_RATE || source.Format.Channels != 1)
        {
            throw new InvalidDataException("分析会话要求 48 kHz 单声道 PCM。");
        }
        if (pcmReader is not null && lastPcmTile is { } previous && previous.Key.SamplesPerColumn == tileSamples &&
            previous.Key.Mode == work.Request.Mode && previous.Key.Index + 1 == index)
        {
            var previousFirst = previous.Key.Index * tileSamples - AudioAnalysisSampleReader.PADDING;
            var overlapEnd = Math.Min(end, pcmReadThrough);
            if (overlapEnd > start)
            {
                var length = checked((int)(overlapEnd - start));
                previous.Samples.Span.Slice((int)(start - previousFirst), length).CopyTo(samples.AsSpan((int)(start - first), length));
                start = overlapEnd;
            }
        }
        else
        {
            source.Seek(new(start, WaveformAnalyzer.SAMPLE_RATE), lifetime.Token);
            Check(work);
            pcmReader = new(source, CheckActiveRequest, lifetime.Token);
        }
        pcmReader.CopyTo(start, samples.AsSpan((int)(start - first), checked((int)(end - start))));
        if (pcmReader.EndSample is { } sourceEnd)
        {
            confirmedSourceEnd = Math.Min(sourceEnd, confirmedSourceEnd ?? sourceEnd);
        }
        var tile = new AudioAnalysisTile(key, null, null, samples);
        Save(tile, work);
        lastPcmTile = tile;
        pcmReadThrough = Math.Min(end, confirmedSourceEnd ?? end);
        return tile;
    }

    private void ResetPcmCursor()
    {
        pcmReader = null;
        lastPcmTile = null;
    }

    private void CheckActiveRequest()
    {
        Check(active ?? throw new OperationCanceledException());
    }

    private static long Ceiling(long value, long step)
    {
        return -AudioSpectrumWindowAnalyzer.Floor(-value, step) / step;
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
