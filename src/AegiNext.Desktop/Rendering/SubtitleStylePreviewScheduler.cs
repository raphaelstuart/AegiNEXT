using AegiNext.Media.Preview;

namespace AegiNext.Desktop.Rendering;

internal sealed class SubtitleStylePreviewScheduler(
    Func<SubtitleStylePreviewRequest, CancellationToken, SdrVideoFrame> render,
    Action<SubtitleStylePreviewResult> present) : IDisposable
{
    private readonly object gate = new();
    private SubtitleStylePreviewRequest? pending;
    private CancellationTokenSource? activeCancellation;
    private Task? worker;
    private long latestRevision;
    private bool disposed;

    internal Task Completion
    {
        get
        {
            lock (gate)
            {
                return worker ?? Task.CompletedTask;
            }
        }
    }

    internal void Submit(SubtitleStylePreviewRequest request)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            latestRevision = request.Revision;
            pending = request;
            activeCancellation?.Cancel();
            worker ??= Task.Run(Process);
        }
    }

    internal void Invalidate(long revision)
    {
        lock (gate)
        {
            latestRevision = revision;
            pending = null;
            activeCancellation?.Cancel();
        }
    }

    private void Process()
    {
        while (true)
        {
            SubtitleStylePreviewRequest request;
            CancellationTokenSource cancellation;
            lock (gate)
            {
                if (disposed || pending is null)
                {
                    worker = null;
                    return;
                }
                request = pending;
                pending = null;
                activeCancellation = cancellation = new();
            }
            SdrVideoFrame? frame = null;
            Exception? error = null;
            try
            {
                frame = render(request, cancellation.Token);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
            }
            catch (Exception failure)
            {
                error = failure;
            }
            lock (gate)
            {
                if (!disposed && !cancellation.IsCancellationRequested && request.Revision == latestRevision)
                {
                    present(new(request, frame, error));
                }
                activeCancellation = null;
                cancellation.Dispose();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (gate)
        {
            disposed = true;
            pending = null;
            activeCancellation?.Cancel();
        }
    }
}
