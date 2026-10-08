namespace AegiNext.Media.Analysis;

internal sealed record AudioAnalysisSegmentWork(AudioAnalysisCacheSegment Segment, Task<ReadOnlyMemory<byte>> Computation);
