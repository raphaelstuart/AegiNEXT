namespace AegiNext.Media.Analysis;

/// <summary>在多个音频会话之间公平分配解码及 DSP 的 CPU 配额。</summary>
public sealed class AudioAnalysisWorkerBudget : IDisposable
{
    private const int MAXIMUM_FOREGROUND_STREAK = 3;
    private readonly Lock gate = new();
    private readonly LinkedList<AudioAnalysisWorkerRequest> pending = new();
    private readonly int processorCount;
    private int maximumWorkers;
    private int activeWorkers;
    private int foregroundStreak;
    private object? lastForegroundOwner;
    private object? lastBackgroundOwner;
    private bool disposed;

    /// <summary>创建共享预算；零表示自动保留可用的交互余量。</summary>
    public AudioAnalysisWorkerBudget(int maximumWorkers = 0) : this(maximumWorkers, Environment.ProcessorCount)
    {
    }

    internal AudioAnalysisWorkerBudget(int maximumWorkers, int processorCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processorCount);
        this.processorCount = processorCount;
        this.maximumWorkers = NormalizeMaximum(maximumWorkers);
    }

    public int MaximumWorkers
    {
        get
        {
            lock (gate)
            {
                return maximumWorkers;
            }
        }
    }

    public int ActiveWorkers
    {
        get
        {
            lock (gate)
            {
                return activeWorkers;
            }
        }
    }

    /// <summary>更新总并行上限；降低上限不会中断已获配额的工作。</summary>
    public void UpdateMaximumWorkers(int value)
    {
        var normalized = NormalizeMaximum(value);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            maximumWorkers = normalized;
            GrantPending();
        }
    }

    /// <summary>申请一个 CPU 配额；前台优先，同一优先级按会话轮转。</summary>
    public ValueTask<AudioAnalysisWorkerLease> AcquireAsync(object owner, bool foreground = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        AudioAnalysisWorkerRequest request;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            request = new(this, owner, foreground, cancellationToken);
            request.Node = pending.AddLast(request);
            GrantPending();
        }
        return WaitForRequestAsync(request);
    }

    /// <summary>取消全部排队申请；已运行的工作仍可正常释放配额。</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }
            disposed = true;
            foreach (var request in pending)
            {
                request.Node = null;
                request.Completion.TrySetCanceled(new(true));
            }
            pending.Clear();
            lastForegroundOwner = null;
            lastBackgroundOwner = null;
        }
    }

    internal void Release()
    {
        lock (gate)
        {
            activeWorkers--;
            if (!disposed)
            {
                GrantPending();
            }
        }
    }

    private static async ValueTask<AudioAnalysisWorkerLease> WaitForRequestAsync(AudioAnalysisWorkerRequest request)
    {
        using var registration = request.CancellationToken.UnsafeRegister(static state =>
        {
            var pendingRequest = (AudioAnalysisWorkerRequest)state!;
            pendingRequest.Budget.Cancel(pendingRequest);
        }, request);
        return await request.Completion.Task.ConfigureAwait(false);
    }

    private void Cancel(AudioAnalysisWorkerRequest request)
    {
        lock (gate)
        {
            if (request.Node is null)
            {
                return;
            }
            pending.Remove(request.Node);
            request.Node = null;
            request.Completion.TrySetCanceled(request.CancellationToken);
            GrantPending();
        }
    }

    private void GrantPending()
    {
        while (activeWorkers < maximumWorkers && pending.Count != 0)
        {
            var foreground = pending.Any(static request => request.Foreground) &&
                (foregroundStreak < MAXIMUM_FOREGROUND_STREAK || pending.All(static request => request.Foreground));
            var previousOwner = foreground ? lastForegroundOwner : lastBackgroundOwner;
            var node = pending.First;
            LinkedListNode<AudioAnalysisWorkerRequest>? first = null;
            while (node is not null)
            {
                if (node.Value.Foreground == foreground)
                {
                    first ??= node;
                    if (!ReferenceEquals(node.Value.Owner, previousOwner))
                    {
                        break;
                    }
                }
                node = node.Next;
            }
            node ??= first!;
            var request = node.Value;
            pending.Remove(node);
            request.Node = null;
            activeWorkers++;
            if (foreground)
            {
                foregroundStreak = Math.Min(MAXIMUM_FOREGROUND_STREAK, foregroundStreak + 1);
                lastForegroundOwner = request.Owner;
            }
            else
            {
                foregroundStreak = 0;
                lastBackgroundOwner = request.Owner;
            }
            request.Completion.TrySetResult(new(this));
        }
    }

    private int NormalizeMaximum(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value, 1024);
        return value == 0 ? Math.Min(4, Math.Max(1, processorCount - 2)) : Math.Clamp(value, 1, Math.Max(1, processorCount - 1));
    }
}
