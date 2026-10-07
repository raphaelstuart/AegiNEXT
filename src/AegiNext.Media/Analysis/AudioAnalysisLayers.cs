namespace AegiNext.Media.Analysis;

/// <summary>仅包含请求显示的分析层；隐藏层不计算，也不以空白数据代替。</summary>
public sealed record AudioAnalysisLayers(WaveformData? Waveform, SpectrogramData? Spectrogram);
