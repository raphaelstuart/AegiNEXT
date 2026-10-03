using System.Numerics;

namespace AegiNext.Rendering;

/// <summary>
/// 字体内 glyph ID、源文本 UTF-16 cluster 下标及相对基线位置；一个 cluster 可包含多个字符。
/// </summary>
public readonly record struct ShapedGlyph(ushort GlyphId, int Utf16Cluster, Vector2 Position);
