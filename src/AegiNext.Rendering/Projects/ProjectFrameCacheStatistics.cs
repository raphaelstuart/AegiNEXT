namespace AegiNext.Rendering.Projects;

/// <summary>工程前景缓存的累计工作量及可选阶段计时。</summary>
public readonly record struct ProjectFrameCacheStatistics(ulong Evaluations, ulong Redraws, ulong Copies,
    ulong CopiedBytes, double EvaluateMilliseconds, double DrawMilliseconds, double CopyMilliseconds);
