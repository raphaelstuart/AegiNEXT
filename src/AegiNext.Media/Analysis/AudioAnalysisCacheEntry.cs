namespace AegiNext.Media.Analysis;

internal sealed record AudioAnalysisCacheEntry(AudioAnalysisTileKind Kind, int Resolution, long FirstColumn,
    int Columns, long Offset, int Length, byte[] Digest);
