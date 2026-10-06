using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Projects;

public sealed partial class ProjectSceneRenderer
{
    private void ShapeFontRuns(ProjectDocument document, SubtitleStyle style, string text, int offset,
        TextDirection direction, ImmutableArray<SubtitleLayoutRun>.Builder runs)
    {
        if (style.FontAssetId.HasValue)
        {
            AddFontRun(GetPreferredTextShaper(document, style), style, text, offset, direction, runs);
            return;
        }
        var boundaries = StringInfo.ParseCombiningCharacters(text);
        var begin = 0;
        var firstEnd = boundaries.Length > 1 ? boundaries[1] : text.Length;
        var current = GetSystemTextShaper(document, style, text[..firstEnd]);
        for (var index = 1; index < boundaries.Length; index++)
        {
            var start = boundaries[index];
            var end = index + 1 < boundaries.Length ? boundaries[index + 1] : text.Length;
            var next = GetSystemTextShaper(document, style, text[start..end]);
            if (ReferenceEquals(current, next))
            {
                continue;
            }
            AddFontRun(current, style, text[begin..start], offset + begin, direction, runs);
            begin = start;
            current = next;
        }
        AddFontRun(current, style, text[begin..], offset + begin, direction, runs);
    }

    private static void AddFontRun(TextShaper shaper, SubtitleStyle style, string text, int offset,
        TextDirection direction, ImmutableArray<SubtitleLayoutRun>.Builder runs)
    {
        var shape = shaper.Shape(text, (float)style.FontSize, direction, "und");
        runs.Add(new(text, offset, style, shape, direction) { ResolvedFontFamily = shaper.FontFamily });
    }

    private TextShaper GetPreferredTextShaper(ProjectDocument document, SubtitleStyle style)
    {
        var key = (style.FontAssetId, style.FontFamily, style.Bold, style.Italic);
        if (textShapers.TryGetValue(key, out var existing))
        {
            return existing;
        }
        var result = CacheTextShaper(Typeface(document, style), style);
        textShapers.Add(key, result);
        return result;
    }

    private TextShaper GetSystemTextShaper(ProjectDocument document, SubtitleStyle style, string grapheme)
    {
        var key = (style.FontFamily, style.Bold, style.Italic, grapheme);
        if (resolvedTextShapers.TryGetValue(key, out var existing))
        {
            return existing;
        }
        var preferred = GetPreferredTextShaper(document, style);
        if (preferred.ContainsGlyphs(grapheme))
        {
            resolvedTextShapers.Add(key, preferred);
            return preferred;
        }
        var fontStyle = FontStyle(style);
        foreach (var rune in grapheme.EnumerateRunes())
        {
            if (TextShaper.IsShapingControl(rune))
            {
                continue;
            }
            var candidate = SKFontManager.Default.MatchCharacter(style.FontFamily, fontStyle, ["und"], rune.Value);
            if (candidate is null)
            {
                continue;
            }
            var fallback = CacheTextShaper(candidate, style);
            if (fallback.ContainsGlyphs(grapheme))
            {
                resolvedTextShapers.Add(key, fallback);
                return fallback;
            }
        }
        throw new InvalidDataException($"没有覆盖字素“{grapheme}”的系统字体，请导入项目字体资源。");
    }

    private TextShaper CacheTextShaper(SKTypeface typeface, SubtitleStyle style)
    {
        var key = (typeface.Handle, style.Bold, style.Italic);
        if (actualTextShapers.TryGetValue(key, out var existing))
        {
            if (!existing.UsesTypeface(typeface))
            {
                typeface.Dispose();
            }
            return existing;
        }
        var result = new TextShaper(typeface, style.Bold, style.Italic);
        actualTextShapers.Add(key, result);
        return result;
    }

    private static SKFontStyle FontStyle(SubtitleStyle style)
    {
        return new(style.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal, SKFontStyleWidth.Normal,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
    }
}
