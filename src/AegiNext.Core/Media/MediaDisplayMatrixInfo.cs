namespace AegiNext.Core.Media;

/// <summary>
/// 流级显示矩阵原始文本及报告的旋转角度；旋转角度不替代完整变换矩阵。
/// </summary>
public sealed record MediaDisplayMatrixInfo
{
    public string? MatrixText { get; init; }

    public MediaRatio? RotationDegrees { get; init; }
}
