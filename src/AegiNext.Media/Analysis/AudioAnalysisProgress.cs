using AegiNext.Core.Timing;

namespace AegiNext.Media.Analysis;

/// <summary>表示单次音频缓存构建已完成的工程时间范围。</summary>
public sealed record AudioAnalysisProgress(MediaTime Processed, MediaTime Duration, bool IsComplete);
