using SkiaSharp;

namespace AegiNext.Rendering.Fonts;

internal readonly record struct ResolvedSystemFont(SKTypeface Typeface, SystemFontFace? Face, bool IsExactMatch);
