namespace AegiNext.Media.Analysis;

/// <summary>一个共享 CPU 配额，释放后允许下一个音频工作继续。</summary>
public sealed class AudioAnalysisWorkerLease : IDisposable
{
    private AudioAnalysisWorkerBudget? budget;

    internal AudioAnalysisWorkerLease(AudioAnalysisWorkerBudget budget)
    {
        this.budget = budget;
    }

    /// <summary>释放配额；重复调用不会重复归还。</summary>
    public void Dispose()
    {
        Interlocked.Exchange(ref budget, null)?.Release();
    }
}
