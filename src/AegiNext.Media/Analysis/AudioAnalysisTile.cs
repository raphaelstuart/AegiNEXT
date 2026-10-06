namespace AegiNext.Media.Analysis;

internal sealed record AudioAnalysisTile(AudioAnalysisTileKey Key, WaveformData? Waveform, SpectrogramData? Spectrogram)
{
    internal long Bytes => (long)(Waveform?.Peaks.Length ?? 0) * sizeof(float) + (Spectrogram?.Levels.Length ?? 0);
}
