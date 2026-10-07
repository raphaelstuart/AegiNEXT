namespace AegiNext.Media.Analysis;

/// <summary>区分完整样本分析与可跨缩放比例复用的近似缩略分析。</summary>
public enum AudioAnalysisMode
{
    /// <summary>完整读取当前范围的波形样本，频谱按当前时间列分析。</summary>
    EXACT,

    /// <summary>稀疏采样短窗口，允许重采样已有缩略图；结果不用于精确峰值判断。</summary>
    PREVIEW
}
