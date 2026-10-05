namespace AegiNext.Rendering.Projects;

/// <summary>字幕局部坐标的字素安全命中结果；Utf16Offset 为实际插入位置。</summary>
public sealed record SubtitleTextHit(int Utf16Offset, int GraphemeStart, int GraphemeLength, bool IsTrailing);
