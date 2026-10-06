namespace AegiNext.Core.Projects;

/// <summary>蒙版围绕固定工程坐标轴心的独立缩放、角度旋转及平移。</summary>
public sealed record MaskTransform
{
    public ScenePoint Position { get; init; }
    public ScenePoint Scale { get; init; } = new(1, 1);
    public double Rotation { get; init; }
    public ScenePoint Pivot { get; init; }
}
