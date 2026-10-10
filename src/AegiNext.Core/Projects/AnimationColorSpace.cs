namespace AegiNext.Core.Projects;

/// <summary>颜色轨道的 RGB 插值空间；存储值与求值结果始终为直通线性颜色，Alpha 不编码。</summary>
public enum AnimationColorSpace
{
    LINEAR_RGB = 0,
    SRGB = 1
}
