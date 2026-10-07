using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

/// <summary>验证快速窗口替换时，已取消请求不能破坏仍有效的最新分析任务。</summary>
public sealed class AudioAnalysisCancellationTests
{
    /// <summary>两种入口都在接纳前拒绝取消请求，保护正在执行和仍在排队的最新任务。</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AlreadyCanceledRequestsCannotReplaceActiveOrQueuedLatestWork(bool overview, bool queued)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = BlockFirstRead(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 20L, entered, release);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(20));
        var active = Submit(session, new(MediaTime.Zero, 512, 64), overview);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var latest = queued ? Submit(session, new(new(10), 512, 64), overview) : active;
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        Task rejected;
        try
        {
            rejected = Submit(session, new(new(2), 512, 64), overview, canceled.Token);
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
        Assert.Equal(0, source.CancelCount);
        Assert.Equal(queued ? 2 : 1, source.SeekCount);
    }

    /// <summary>已关闭入口仍优先抛出释放异常，不因传入取消令牌改变原有契约。</summary>
    [Fact]
    public async Task ClosedSessionsKeepTheirDisposedContractEvenForAlreadyCanceledRequests()
    {
        await using var session = new AudioAnalysisSession(_ => throw new InvalidOperationException("不应打开 PCM 源。"),
            new(MediaTime.Zero), new(20));
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

    /// <summary>接纳后发生的真实取消仍终止该任务，随后有效请求复用同一个解码器完成。</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationAfterAdmissionStillCancelsOnlyItsOwnWork(bool overview)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var source = BlockFirstRead(_ => 0.5F, WaveformAnalyzer.SAMPLE_RATE * 20L, entered, release);
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(20));
        using var cancellation = new CancellationTokenSource();
        var pending = Submit(session, new(MediaTime.Zero, 512, 64), overview, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        release.Set();
        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(cancellation.Token, error.CancellationToken);
        var latest = await session.GetWindowAsync(new(new(10), 512, 64), false).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0.5F, latest.Waveform.Peaks.Span[1]);
        Assert.Equal(0, source.CancelCount);
    }

    /// <summary>两三小时媒体连续缩放替换后，最终局部窗口完整返回且读取量保持有界。</summary>
    [Theory]
    [InlineData(7200)]
    [InlineData(10800)]
    public async Task RapidReplacementsDeliverTheCompleteFinalLongMediaWindow(int duration)
    {
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        static float Sample(long index) => (float)(0.5 * Math.Sin(index * 2 * Math.PI * 1000 / WaveformAnalyzer.SAMPLE_RATE));
        var source = BlockFirstRead(Sample, (long)duration * WaveformAnalyzer.SAMPLE_RATE, entered, release);
        const long CACHE_BYTES = 8L * 1024 * 1024;
        await using var session = new AudioAnalysisSession(_ => source, new(MediaTime.Zero), new(duration), CACHE_BYTES);
        var first = session.GetWindowAsync(new(new(duration / 2), 512, 64), true);
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
                var request = new WaveformAnalysisRequest(new((duration - 10) * 8L + index, 8), 32 << index % 8, 64);
                superseded.Add(session.GetWindowAsync(request, true));
                _ = session.GetLayersAsync(request, true, true, canceled.Token);
            }
            final = session.GetWindowAsync(finalRequest, true);
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
        var spectrum = Assert.IsType<SpectrogramData>(result.Spectrogram);
        var row = (int)(Math.Log(1000.0 / 40) / Math.Log(8000.0 / 40) * spectrum.Height);
        Assert.True(spectrum.Width > 0);
        foreach (var level in spectrum.Levels.Span.Slice(row * spectrum.Width, spectrum.Width))
        {
            Assert.True(level > 150);
        }
        Assert.InRange(source.FramesRead, 1, 250000);
        Assert.InRange(source.SeekCount, 1, 3);
        Assert.InRange(session.CachedBytes, 1, CACHE_BYTES);
        Assert.Equal(0, source.CancelCount);
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
