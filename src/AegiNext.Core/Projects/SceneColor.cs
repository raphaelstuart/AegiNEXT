namespace AegiNext.Core.Projects;

/// <summary>以工程参考白为标度的直通线性 sRGB；RGB 可超过 1，Alpha 属于 [0,1]。</summary>
public readonly record struct SceneColor(double Red, double Green, double Blue, double Alpha = 1)
{
    public static SceneColor White => new(1, 1, 1);
    public static SceneColor Black => new(0, 0, 0);
    public static SceneColor Transparent => new(0, 0, 0, 0);
}
