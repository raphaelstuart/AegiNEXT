namespace AegiNext.Media.Analysis;

internal sealed record AudioAnalysisCacheSegment(long FirstSample, long Start, long End, long FirstColumn,
    int Columns, float[] Samples);
