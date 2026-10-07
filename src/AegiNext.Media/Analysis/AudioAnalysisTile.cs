namespace AegiNext.Media.Analysis;

internal sealed record AudioAnalysisTile(AudioAnalysisTileKey Key, WaveformData? Waveform, SpectrogramData? Spectrogram,
    ReadOnlyMemory<float> Samples = default)
{
    internal long Bytes => (long)((Waveform?.Peaks.Length ?? 0) + Samples.Length) * sizeof(float) +
                           (Spectrogram?.Levels.Length ?? 0);
}
