namespace AegiNext.Rendering;

/// <summary>临时 F16 表面的累计分配、复用、释放及当前空闲保留内存；活跃 lease 不计入保留预算。</summary>
public readonly record struct RenderSurfacePoolStatistics(long Allocations, long Reuses, long ReleasedSurfaces,
    long RetainedBytes, int RetainedSurfaces, int ActiveLeases, int PeakActiveLeases, long MaximumRetainedBytes);
