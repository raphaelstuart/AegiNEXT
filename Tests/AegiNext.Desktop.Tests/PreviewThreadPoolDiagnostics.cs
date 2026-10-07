namespace AegiNext.Desktop.Tests;

internal static class PreviewThreadPoolDiagnostics
{
    internal static string Read()
    {
        ThreadPool.GetMinThreads(out var minimumWorkers, out var minimumIo);
        ThreadPool.GetAvailableThreads(out var availableWorkers, out var availableIo);
        return $"ProcessorCount={Environment.ProcessorCount}; PoolThreads={ThreadPool.ThreadCount}; " +
            $"PoolPending={ThreadPool.PendingWorkItemCount}; PoolMinWorkers={minimumWorkers}; PoolMinIo={minimumIo}; " +
            $"PoolAvailableWorkers={availableWorkers}; PoolAvailableIo={availableIo}";
    }
}
