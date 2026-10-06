namespace AegiNext.Media.Analysis;

/// <summary>独立的局部波形和频谱快照，缓存淘汰或分析会话关闭不会改变已交付的数据。</summary>
public sealed record AudioAnalysisWindow(WaveformData Waveform, SpectrogramData? Spectrogram);
