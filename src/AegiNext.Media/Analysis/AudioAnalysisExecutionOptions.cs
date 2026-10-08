namespace AegiNext.Media.Analysis;

/// <summary>可在安全边界更新、不改变缓存身份的执行参数。</summary>
public sealed record AudioAnalysisExecutionOptions
{
    public int MaximumWorkers { get; init; }
    public int MemoryBudgetMiB { get; init; } = 64;
    public int SegmentSamples { get; init; } = 196608;
    public static int HardwareMaximumWorkers => Math.Max(1, Environment.ProcessorCount - 1);
    public int EffectiveMaximumWorkers => MaximumWorkers == 0
        ? Math.Min(4, Math.Max(1, Environment.ProcessorCount - 2))
        : Math.Clamp(MaximumWorkers, 1, HardwareMaximumWorkers);
    public long ReadCacheBytes => (long)MemoryBudgetMiB * 1024 * 1024 / 4;
    public long WorkingBytes => (long)MemoryBudgetMiB * 1024 * 1024 * 3 / 4;

    /// <summary>验证可移植的线程请求与有界缓冲配置；较少核心机器只限制实际并行数。</summary>
    public void Validate()
    {
        if (MaximumWorkers is < 0 or > 1024 || MemoryBudgetMiB is < 32 or > 512 ||
            SegmentSamples is not (49152 or 98304 or 196608 or 393216 or 786432))
        {
            throw new InvalidDataException("音频分析执行参数无效。");
        }
    }
}
