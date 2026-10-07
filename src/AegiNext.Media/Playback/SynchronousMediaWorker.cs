using System.Threading.Channels;

namespace AegiNext.Media.Playback;

/// <summary>以单个专用后台线程串行执行同步媒体操作，至多保留一个待执行任务。</summary>
public sealed class SynchronousMediaWorker : IAsyncDisposable
{
    private readonly Channel<Action> jobs = Channel.CreateBounded<Action>(new BoundedChannelOptions(1)
    {
        SingleReader = true,
        SingleWriter = false,
        AllowSynchronousContinuations = false,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly TaskCompletionSource stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Thread thread;
    private int closing;

    /// <summary>为一个媒体资源所有者创建具名线程；资源创建、使用和销毁应经由同一实例执行。</summary>
    public SynchronousMediaWorker(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        thread = new(Run)
        {
            IsBackground = true,
            Name = name
        };
        thread.Start();
    }

    /// <summary>等待操作的实际结果；运行中的任务不会因调用方取消等待而遗失其拥有的媒体资源。</summary>
    public async Task<T> ExecuteAsync<T>(Func<T> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        ThrowIfReentrant();
        ObjectDisposedException.ThrowIf(Volatile.Read(ref closing) != 0, this);
        cancellationToken.ThrowIfCancellationRequested();
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            await jobs.Writer.WriteAsync(() =>
            {
                try
                {
                    if (Volatile.Read(ref closing) != 0)
                    {
                        completion.TrySetCanceled(new(true));
                        return;
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    completion.TrySetResult(action());
                }
                catch (OperationCanceledException cancelled)
                {
                    completion.TrySetCanceled(cancelled.CancellationToken);
                }
                catch (Exception error)
                {
                    completion.TrySetException(error);
                }
            }, cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException)
        {
            throw new ObjectDisposedException(nameof(SynchronousMediaWorker));
        }
        return await completion.Task.ConfigureAwait(false);
    }

    /// <summary>串行执行没有返回值的操作，并等待其实际完成。</summary>
    public Task ExecuteAsync(Action action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action);
        return ExecuteAsync(() =>
        {
            action();
            return true;
        }, cancellationToken);
    }

    /// <summary>拒绝新任务、取消尚未开始的任务，并异步等待运行中的任务归还资源后退出线程。</summary>
    public ValueTask DisposeAsync()
    {
        ThrowIfReentrant();
        if (Interlocked.Exchange(ref closing, 1) == 0)
        {
            jobs.Writer.TryComplete();
        }
        return new(stopped.Task);
    }

    private void ThrowIfReentrant()
    {
        if (Environment.CurrentManagedThreadId == thread.ManagedThreadId)
        {
            throw new InvalidOperationException("媒体专用线程不能重入或等待自身关闭。");
        }
    }

    private void Run()
    {
        try
        {
            while (jobs.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (jobs.Reader.TryRead(out var job))
                {
                    job();
                }
            }
        }
        finally
        {
            stopped.TrySetResult();
        }
    }
}
