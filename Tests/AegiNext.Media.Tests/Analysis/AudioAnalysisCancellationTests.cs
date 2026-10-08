using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

/// <summary>验证视口替换仅取消读取等待，完整缓存构建和独立细节解码仍可继续。</summary>
public sealed class AudioAnalysisCancellationTests
{
    /// <summary>接纳前拒绝取消请求，不替换当前有效视口，也不重启完整扫描。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AlreadyCanceledRequestsCannotReplaceActiveOrQueuedLatestWork(bool overview, bool queued)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = BlockFirstRead(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 4L, entered, release);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(4));
        var active = Submit(session, new(MediaTime.Zero, 512, 64), overview);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latest = queued ? Submit(session, new(new(2), 512, 64), overview) : active;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Task rejected;
        try
        {
            rejected = Submit(session, new(new(1), 512, 64), overview, canceled.Token);
            Assert.False(latest.IsCompleted);
            Assert.True(rejected.IsCanceled);
        }
        finally
        {
            release.Set();
        }
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => rejected);
        Assert.Equal(canceled.Token, error.CancellationToken);
        await latest.WaitAsync(TimeSpan.FromSeconds(5));
        if (queued)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => active);
        }
        await session.PrepareCacheAsync().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, source.CancelCount);
        Assert.Equal(1, source.SeekCount);
        Assert.Equal(4L * WaveformAnalyzer.SAMPLE_RATE, source.FramesRead);
    }

    /// <summary>已关闭入口仍优先抛出释放异常，不因传入取消令牌改变原有契约。</summary>
    [Fact]
    public async Task ClosedSessionsKeepTheirDisposedContractEvenForAlreadyCanceledRequests()
    {
        await using var session = new AudioAnalysisSession(_ => throw new InvalidOperationException("不应打开 PCM 源。"),
            new(MediaTime.Zero), new(4));
        await session.DisposeAsync();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var request = new WaveformAnalysisRequest(MediaTime.Zero, 512, 64);
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = session.GetLayersAsync(request, true, true, canceled.Token);
        });
        Assert.Throws<ObjectDisposedException>(() =>
        {
            _ = session.GetOverviewAsync(request, true, canceled.Token);
        });
    }

    /// <summary>视口取消及时结束等待，缓存构建继续完成，并服务后续有效请求。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterAdmissionLeavesTheSingleCacheScanUsable(bool overview)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = BlockFirstRead(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 4L, entered, release);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(4));
        using var cancellation = new CancellationTokenSource();
        var pending = Submit(session, new(MediaTime.Zero, 512, 64), overview, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        try
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Equal(0, source.CancelCount);
        }
        finally
        {
            release.Set();
        }
        await session.PrepareCacheAsync().WaitAsync(TimeSpan.FromSeconds(5));
        var reads = source.ReadCount;
        var latest = await session.GetWindowAsync(new(new(2), 512, 64), false).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0.5F, latest.Waveform.Peaks.Span[1]);
        Assert.Equal(reads, source.ReadCount);
        Assert.Equal(1, source.SeekCount);
        Assert.Equal(0, source.CancelCount);
    }

    /// <summary>小时级媒体的极细波形替换只读取最终局部范围，不启动全片缓存。</summary>
    [Theory]
    [InlineData(7200)]
    [InlineData(10800)]
    public async Task RapidFineWaveformReplacementsDeliverTheCompleteFinalLongMediaWindow(int duration)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        static float Sample(long index) => (float)(0.5 * Math.Sin(index * 2 * Math.PI * 1000 / WaveformAnalyzer.SAMPLE_RATE));
        var source = BlockFirstRead(Sample, (long)duration * WaveformAnalyzer.SAMPLE_RATE, entered, release);
        const long CACHE_BYTES = 8L * 1024 * 1024;
        var builds = 0;
        await using var session = new AudioAnalysisSession(_ =>
        {
            Interlocked.Increment(ref builds);
            throw new InvalidOperationException("极细波形不得打开完整扫描源。");
        }, new(MediaTime.Zero), new(duration), CACHE_BYTES, detailSourceFactory: _ => source);
        var first = session.GetWindowAsync(new(new(duration / 2), 32, 64), false);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var superseded = new List<Task<AudioAnalysisWindow>>();
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var finalRequest = new WaveformAnalysisRequest(new(duration - 2), 32, 2048);
        Task<AudioAnalysisWindow> final;
        try
        {
            for (var index = 0; index < 40; index++)
            {
                var request = new WaveformAnalysisRequest(new((duration - 10) * 8L + index, 8), 1 << index % 8, 64);
                superseded.Add(session.GetWindowAsync(request, false));
                _ = session.GetLayersAsync(request, true, false, canceled.Token);
            }
            final = session.GetWindowAsync(finalRequest, false);
            var rejected = session.GetLayersAsync(new(MediaTime.Zero, 512, 64), true, true, canceled.Token);
            Assert.True(rejected.IsCanceled);
            Assert.False(final.IsCompleted);
        }
        finally
        {
            release.Set();
        }
        var result = await final.WaitAsync(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        foreach (var task in superseded)
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        }
        Assert.Equal(finalRequest, result.Waveform.Request);
        var peaks = result.Waveform.Peaks.ToArray();
        Assert.Equal(finalRequest.BucketCount * 2, peaks.Length);
        for (var bucket = 0; bucket < finalRequest.BucketCount; bucket++)
        {
            Assert.True(peaks[bucket * 2] < -0.25F || peaks[bucket * 2 + 1] > 0.25F);
        }
        Assert.Null(result.Spectrogram);
        Assert.InRange(source.FramesRead, 1, 250000);
        Assert.InRange(source.SeekCount, 1, 3);
        Assert.InRange(session.CachedBytes, 1, CACHE_BYTES);
        Assert.Equal(0, source.CancelCount);
        Assert.Equal(0, builds);
        Assert.False(session.IsCacheComplete);
    }

    private static WindowAudioSource BlockFirstRead(Func<long, float> sample, long endSample,
        TaskCompletionSource entered, ManualResetEventSlim release)
    {
        var reads = 0;
        return new(sample, endSample)
        {
            BeforeRead = token =>
            {
                if (Interlocked.Increment(ref reads) == 1)
                {
                    entered.TrySetResult();
                    release.Wait(token);
                }
            }
        };
    }

    private static Task Submit(AudioAnalysisSession session, WaveformAnalysisRequest request, bool overview,
        CancellationToken cancellationToken = default)
    {
        if (overview)
        {
            return session.GetOverviewAsync(request, true, cancellationToken);
        }
        return session.GetLayersAsync(request, true, true, cancellationToken);
    }
}
