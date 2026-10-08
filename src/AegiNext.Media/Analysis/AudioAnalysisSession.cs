using AegiNext.Core.Timing;
using AegiNext.Media.Audio;

namespace AegiNext.Media.Analysis;

/// <summary>管理单次持久缓存构建、只读视口与独立的极细波形解码。</summary>
public sealed class AudioAnalysisSession : IAsyncDisposable
{
    private const long DEFAULT_CACHE_BYTES = 16L * 1024 * 1024;
    private readonly Lock gate = new();
    private readonly AudioAnalysisCacheStore store;
    private readonly AudioAnalysisCacheBuilder builder;
    private readonly AudioWaveformDetailProvider detail;
    private AudioAnalysisOptions options;
    private readonly CancellationTokenSource lifetime = new();
    private readonly HashSet<Task> reads = [];
    private readonly SemaphoreSlim migrationGate = new(1);
    private readonly string? temporaryDirectory;
    private CancellationTokenSource? viewportCancellation;
    private Task? preparation;
    private Task? closing;
    private bool closed;

    /// <summary>注入各自独占的扫描与细节源；省略目录时使用随会话清理的临时缓存。</summary>
    public AudioAnalysisSession(Func<CancellationToken, IAudioSampleSource> sourceFactory, MediaTimelineMapping mapping,
        MediaTime duration, long maximumCachedBytes = DEFAULT_CACHE_BYTES, string? cacheDirectory = null,
        string? cacheIdentity = null, Func<CancellationToken, IAudioSampleSource>? detailSourceFactory = null,
        int maximumWorkers = 0, AudioAnalysisOptions? options = null, AudioAnalysisWorkerBudget? workerBudget = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFactory);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, MediaTime.Zero);
        ArgumentOutOfRangeException.ThrowIfNegative(maximumCachedBytes);
        this.options = options ?? new()
        {
            Execution = new() { MaximumWorkers = maximumWorkers }
        };
        this.options.Validate();
        if (options is not null)
        {
            maximumCachedBytes = options.Execution.ReadCacheBytes;
        }
        Duration = duration;
        if (cacheDirectory is null)
        {
            temporaryDirectory = Path.Combine(Path.GetTempPath(), "AegiNext", "audio-analysis", Guid.NewGuid().ToString("N"));
            cacheDirectory = temporaryDirectory;
        }
        var summaryBytes = Math.Min(maximumCachedBytes / 4, 16L * 1024 * 1024);
        store = new(cacheDirectory, cacheIdentity ?? Guid.NewGuid().ToString("N"), mapping, duration, summaryBytes, this.options.Recipe);
        builder = new(sourceFactory, mapping, duration, store, maximumWorkers, options: this.options, budget: workerBudget);
        detail = new(detailSourceFactory ?? sourceFactory, mapping, duration, maximumCachedBytes - summaryBytes, workerBudget);
        store.Changed += OnCacheChanged;
    }

    public MediaTime Duration { get; }
    public bool IsCacheComplete => store.IsComplete;
    public string CacheDirectory => store.DirectoryPath;
    public AudioAnalysisOptions Options
    {
        get
        {
            lock (gate)
            {
                return options;
            }
        }
    }
    public int EffectiveWorkers => builder.EffectiveWorkers;
    internal long CachedBytes => store.CachedBytes + detail.CachedBytes;

    /// <summary>已提交缓存更新通知，可能来自工作线程。</summary>
    public event EventHandler? CacheUpdated;

    /// <summary>为独立分析调用创建临时缓存；工程工作区应显式指定缓存目录。</summary>
    public static AudioAnalysisSession Open(string path, int streamIndex, MediaTimelineMapping mapping, MediaTime duration)
    {
        return OpenCore(path, streamIndex, mapping, duration, null);
    }

    /// <summary>打开工程缓存；命中缓存时不创建解码器。</summary>
    public static AudioAnalysisSession Open(string path, int streamIndex, MediaTimelineMapping mapping, MediaTime duration,
        string cacheDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        return OpenCore(path, streamIndex, mapping, duration, cacheDirectory);
    }

    /// <summary>按已验证配置打开工程缓存，并共享应用的分析 CPU 预算。</summary>
    public static AudioAnalysisSession Open(string path, int streamIndex, MediaTimelineMapping mapping, MediaTime duration,
        string cacheDirectory, AudioAnalysisOptions options, AudioAnalysisWorkerBudget? workerBudget = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        return OpenCore(path, streamIndex, mapping, duration, cacheDirectory, options, workerBudget);
    }

    private static AudioAnalysisSession OpenCore(string path, int streamIndex, MediaTimelineMapping mapping,
        MediaTime duration, string? cacheDirectory, AudioAnalysisOptions? options = null, AudioAnalysisWorkerBudget? workerBudget = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfNegative(streamIndex);
        path = Path.GetFullPath(path);
        var identity = AudioAnalysisCacheIdentity.Create(path, streamIndex, mapping, duration, options?.Recipe);
        var session = new AudioAnalysisSession(
            token => FfmpegAudioDecoder.Open(path, streamIndex, new(WaveformAnalyzer.SAMPLE_RATE, 1), token),
            mapping, duration, cacheDirectory: cacheDirectory, cacheIdentity: identity, options: options, workerBudget: workerBudget);
        session.store.ValidateSource = () =>
        {
            if (AudioAnalysisCacheIdentity.Create(path, streamIndex, mapping, duration, options?.Recipe) != identity)
            {
                throw new IOException("音频缓存构建期间媒体文件发生变化。");
            }
        };
        return session;
    }

    /// <summary>更新执行预算；DSP 在当前批次排空后采用新配置，缓存内容保持相同。</summary>
    public void UpdateExecutionOptions(AudioAnalysisExecutionOptions value)
    {
        ArgumentNullException.ThrowIfNull(value);
        value.Validate();
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            options = options with { Execution = value };
            builder.UpdateExecutionOptions(value);
            var summaryBytes = Math.Min(value.ReadCacheBytes / 4, 16L * 1024 * 1024);
            store.SetMaximumCachedBytes(summaryBytes);
            detail.SetMaximumCachedBytes(value.ReadCacheBytes - summaryBytes);
        }
    }

    /// <summary>显式使空闲缓存失效，供用户重新生成；运行中的构建须先取消并排空。</summary>
    public void InvalidateCache()
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (preparation is { IsCompleted: false })
            {
                throw new InvalidOperationException("运行中的音频缓存须先取消并排空。");
            }
            store.Invalidate();
        }
    }

    /// <summary>构建全片缓存，安全批次边界执行宿主检查点；视口取消不终止构建。</summary>
    public Task PrepareCacheAsync(Func<CancellationToken, Task>? checkpoint = null,
        IProgress<AudioAnalysisProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled(cancellationToken);
            }
            if (store.IsComplete)
            {
                progress?.Report(new(Duration, Duration, true));
                return Task.CompletedTask;
            }
            if (preparation is null || preparation.IsCompleted)
            {
                store.Reset();
                preparation = Task.Run(async () =>
                {
                    using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
                    await builder.BuildAsync(checkpoint, progress, cancellation.Token).ConfigureAwait(false);
                }, CancellationToken.None);
            }
            return preparation;
        }
    }

    /// <summary>读取完整请求窗口，未完成的缓存区间等待后台构建。</summary>
    public Task<AudioAnalysisWindow> GetWindowAsync(WaveformAnalysisRequest request, bool includeSpectrum,
        CancellationToken cancellationToken = default)
    {
        return RequireWaveformAsync(GetLayersAsync(request, true, includeSpectrum, cancellationToken));
    }

    /// <summary>频谱及普通波形从缓存读取，极细波形交由独立解码器。</summary>
    public Task<AudioAnalysisLayers> GetLayersAsync(WaveformAnalysisRequest request, bool includeWaveform, bool includeSpectrum,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(request, includeWaveform, includeSpectrum, false, cancellationToken);
    }

    /// <summary>返回当前已提交区间，尚无数据的层为null；不启动频谱计算。</summary>
    public Task<AudioAnalysisLayers> GetAvailableLayersAsync(WaveformAnalysisRequest request, bool includeWaveform,
        bool includeSpectrum, CancellationToken cancellationToken = default)
    {
        return ReadAsync(request, includeWaveform, includeSpectrum, true, cancellationToken);
    }

    /// <summary>读取全片概览；取消等待不影响构建。</summary>
    public Task<AudioAnalysisWindow> GetOverviewAsync(WaveformAnalysisRequest request, bool includeSpectrum,
        CancellationToken cancellationToken = default)
    {
        return GetWindowAsync(request, includeSpectrum, cancellationToken);
    }

    private Task<AudioAnalysisLayers> ReadAsync(WaveformAnalysisRequest request, bool waveform, bool spectrum,
        bool availableOnly, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            if (cancellationToken.IsCancellationRequested)
            {
                return Task.FromCanceled<AudioAnalysisLayers>(cancellationToken);
            }
            viewportCancellation?.Cancel();
            viewportCancellation?.Dispose();
            viewportCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
            var token = viewportCancellation.Token;
            var task = Task.Run(() => ReadCoreAsync(request, waveform, spectrum, availableOnly, token), CancellationToken.None);
            reads.Add(task);
            _ = ForgetReadAsync(task);
            return task;
        }
    }

    private async Task<AudioAnalysisLayers> ReadCoreAsync(WaveformAnalysisRequest request, bool waveform, bool spectrum,
        bool availableOnly, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var fineCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var fine = waveform && request.SamplesPerBucket < options.Recipe.WaveformBaseSamples;
        var fineWaveform = fine ? detail.GetWaveformAsync(request, fineCancellation.Token) : null;
        if (!availableOnly && (spectrum || waveform && !fine))
        {
            _ = PrepareCacheAsync(cancellationToken: lifetime.Token);
        }
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var observed = store.Revision;
                WaveformData? peaks;
                SpectrogramData? levels;
                try
                {
                    peaks = waveform && !fine ? store.ReadWaveform(request, availableOnly) : null;
                    levels = spectrum ? store.ReadSpectrum(request, availableOnly) : null;
                }
                catch (InvalidDataException error) when (store.IsComplete)
                {
                    store.Invalidate();
                    throw new AudioAnalysisCacheCorruptionException(error.Message, error);
                }
                if (availableOnly || (!waveform || fine || peaks is not null) && (!spectrum || levels is not null))
                {
                    if (fineWaveform is not null)
                    {
                        peaks = await fineWaveform.ConfigureAwait(false);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    return new(peaks, levels);
                }
                await store.WaitForUpdateAsync(observed, cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            if (fineWaveform is not null && !fineWaveform.IsCompletedSuccessfully)
            {
                fineCancellation.Cancel();
                await ((Task)fineWaveform).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            }
        }
    }

    private async Task ForgetReadAsync(Task task)
    {
        await task.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        lock (gate)
        {
            reads.Remove(task);
        }
    }

    private static async Task<AudioAnalysisWindow> RequireWaveformAsync(Task<AudioAnalysisLayers> task)
    {
        var result = await task.ConfigureAwait(false);
        return new(result.Waveform ?? throw new InvalidOperationException("波形请求没有返回数据。"), result.Spectrogram);
    }

    /// <summary>复制完整缓存到新工程缓存根，复制失败不切换当前读句柄。</summary>
    public Task RelocateCacheAsync(string cacheDirectory, Func<CancellationToken, Task>? checkpoint = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheDirectory);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(closed, this);
            var task = RelocateCoreAsync(cacheDirectory, checkpoint, cancellationToken);
            reads.Add(task);
            _ = ForgetReadAsync(task);
            return task;
        }
    }

    private async Task RelocateCoreAsync(string cacheDirectory, Func<CancellationToken, Task>? checkpoint,
        CancellationToken cancellationToken)
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, cancellationToken);
        await migrationGate.WaitAsync(cancellation.Token).ConfigureAwait(false);
        try
        {
            await store.RelocateAsync(cacheDirectory, checkpoint, cancellation.Token).ConfigureAwait(false);
        }
        finally
        {
            migrationGate.Release();
        }
    }

    private void OnCacheChanged(object? sender, EventArgs args)
    {
        CacheUpdated?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>取消并排空构建、读取和迁移，然后释放源、文件及临时缓存。</summary>
    public ValueTask DisposeAsync()
    {
        lock (gate)
        {
            if (closing is null)
            {
                closed = true;
                viewportCancellation?.Cancel();
                lifetime.Cancel();
                closing = CloseAsync();
            }
            return new(closing);
        }
    }

    private async Task CloseAsync()
    {
        await detail.DisposeAsync().ConfigureAwait(false);
        Task[] pending;
        lock (gate)
        {
            pending = [.. reads, preparation ?? Task.CompletedTask];
        }
        await Task.WhenAll(pending).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        store.Changed -= OnCacheChanged;
        store.Dispose();
        viewportCancellation?.Dispose();
        migrationGate.Dispose();
        lifetime.Dispose();
        if (temporaryDirectory is { } directory && Directory.Exists(directory))
        {
            Directory.Delete(directory, true);
        }
    }
}
