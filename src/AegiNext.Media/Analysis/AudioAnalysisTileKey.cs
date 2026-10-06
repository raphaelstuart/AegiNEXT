namespace AegiNext.Media.Analysis;

internal readonly record struct AudioAnalysisTileKey(bool IsSpectrum, int SamplesPerColumn, long Index);
