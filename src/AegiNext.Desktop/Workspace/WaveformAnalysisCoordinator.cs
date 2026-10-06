using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Media.Analysis;

namespace AegiNext.Desktop.Workspace;

internal sealed class WaveformAnalysisCoordinator : IDisposable
{
    private readonly Action<WaveformData?, WaveformData?, MediaTime> publish;
    private readonly Action<Exception> failed;
    private readonly TimeSpan coalescingDelay;
    private readonly WaveformCache cache = new();
    private Func<WaveformAnalysisRequest, CancellationToken, Task<WaveformData>>? analyze;
    private CancellationTokenSource? mediaCancellation;
    private CancellationTokenSource? detailCancellation;
    private CancellationTokenSource? overviewCancellation;
    private Task detailCompletion = Task.CompletedTask;
    private Task overviewCompletion = Task.CompletedTask;
    private WaveformViewportPlan? desired;
    private WaveformData? detail;
    private WaveformData? overview;
    private MediaTime duration;
    private long revision;
    private bool workerRunning;
    private bool isVisible;
    private bool stopping;
    private bool disposed;

    internal WaveformAnalysisCoordinator(Action<WaveformData?, WaveformData?, MediaTime> publish,
        Action<Exception> failed, TimeSpan? coalescingDelay = null)
    {
        ArgumentNullException.ThrowIfNull(publish);
        ArgumentNullException.ThrowIfNull(failed);
        this.publish = publish;
        this.failed = failed;
        this.coalescingDelay = coalescingDelay ?? TimeSpan.FromMilliseconds(75);
        ArgumentOutOfRangeException.ThrowIfLessThan(this.coalescingDelay, TimeSpan.Zero);
    }

    internal Task Completion => DrainAsync();

    internal async Task StartAsync(Func<WaveformAnalysisRequest, CancellationToken, Task<WaveformData>> analyze,
        MediaTime duration)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        ArgumentNullException.ThrowIfNull(analyze);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(duration, MediaTime.Zero);
        await ClearAsync();
        this.analyze = analyze;
        this.duration = duration;
        mediaCancellation = new();
        stopping = false;
        publish(null, null, duration);
    }

    internal void Request(TimelineViewport viewport, double renderScaling, bool visible)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        isVisible = visible;
        if (analyze is null || stopping)
        {
            return;
        }
        var plan = visible ? WaveformViewportPlanner.Create(viewport, renderScaling, duration) : null;
        if (plan == desired)
        {
            return;
        }
        desired = plan;
        revision++;
        detailCancellation?.Cancel();
        if (plan is null)
        {
            overviewCancellation?.Cancel();
            detail = null;
            publish(null, overview, duration);
            return;
        }
        if (cache.Find(plan.Visible) is { } cached)
        {
            detail = cached;
            publish(detail, overview, duration);
            EnsureOverview();
            return;
        }
        if (!workerRunning)
        {
            workerRunning = true;
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            detailCompletion = completion.Task;
            _ = CompleteAsync(RunDetailsAsync(mediaCancellation!.Token), completion);
        }
    }

    internal void Cancel()
    {
        stopping = true;
        desired = null;
        revision++;
        mediaCancellation?.Cancel();
        detailCancellation?.Cancel();
        overviewCancellation?.Cancel();
    }

    internal async Task ClearAsync()
    {
        Cancel();
        await Completion;
        mediaCancellation?.Dispose();
        mediaCancellation = null;
        analyze = null;
        detail = null;
        overview = null;
        duration = MediaTime.Zero;
        cache.Clear();
        publish(null, null, MediaTime.Zero);
    }

    private async Task RunDetailsAsync(CancellationToken mediaToken)
    {
        try
        {
            while (!mediaToken.IsCancellationRequested && desired is { } plan)
            {
                var version = revision;
                using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(mediaToken);
                detailCancellation = cancellation;
                try
                {
                    await Task.Delay(coalescingDelay, cancellation.Token);
                    var data = cache.Find(plan.Visible) ?? await analyze!(plan.Analysis, cancellation.Token);
                    if (!mediaToken.IsCancellationRequested)
                    {
                        cache.Add(data);
                    }
                    if (!cancellation.IsCancellationRequested && version == revision && !stopping)
                    {
                        detail = data;
                        publish(detail, overview, duration);
                        EnsureOverview();
                    }
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                }
                catch (Exception error)
                {
                    if (!cancellation.IsCancellationRequested && version == revision && !stopping)
                    {
                        failed(error);
                    }
                }
                finally
                {
                    detailCancellation = null;
                }
                if (version == revision)
                {
                    break;
                }
            }
        }
        finally
        {
            workerRunning = false;
        }
    }

    private void EnsureOverview()
    {
        if (overview is not null || overviewCancellation is not null || stopping || !isVisible || analyze is null)
        {
            return;
        }
        var cancellation = CancellationTokenSource.CreateLinkedTokenSource(mediaCancellation!.Token);
        overviewCancellation = cancellation;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        overviewCompletion = completion.Task;
        _ = CompleteAsync(RunOverviewAsync(cancellation), completion);
    }

    private async Task RunOverviewAsync(CancellationTokenSource cancellation)
    {
        try
        {
            var data = await analyze!(WaveformViewportPlanner.CreateOverview(duration), cancellation.Token);
            if (!cancellation.IsCancellationRequested && !stopping)
            {
                overview = data;
                publish(detail, overview, duration);
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
        catch (Exception error)
        {
            if (!cancellation.IsCancellationRequested && !stopping)
            {
                failed(error);
            }
        }
        finally
        {
            var wasCancelled = cancellation.IsCancellationRequested;
            overviewCancellation = null;
            cancellation.Dispose();
            if (wasCancelled && !stopping && isVisible && desired is not null)
            {
                EnsureOverview();
            }
        }
    }

    private async Task DrainAsync()
    {
        Task previousDetail;
        Task previousOverview;
        do
        {
            previousDetail = detailCompletion;
            previousOverview = overviewCompletion;
            await Task.WhenAll(previousDetail, previousOverview);
        }
        while (!ReferenceEquals(previousDetail, detailCompletion) || !ReferenceEquals(previousOverview, overviewCompletion));
    }

    private static async Task CompleteAsync(Task operation, TaskCompletionSource completion)
    {
        try
        {
            await operation;
            completion.TrySetResult();
        }
        catch (Exception error)
        {
            completion.TrySetException(error);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        Cancel();
        disposed = true;
        mediaCancellation?.Dispose();
    }
}
