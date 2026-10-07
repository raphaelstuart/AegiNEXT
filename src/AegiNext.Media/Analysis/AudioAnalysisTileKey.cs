namespace AegiNext.Media.Analysis;

internal readonly record struct AudioAnalysisTileKey(AudioAnalysisTileKind Kind, int SamplesPerColumn, long Index);
