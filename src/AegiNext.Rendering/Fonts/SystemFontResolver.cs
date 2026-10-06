using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Fonts;

internal sealed class SystemFontResolver
{
    private readonly SystemFontCatalog catalog;
    private readonly SKFontManager manager;

    internal SystemFontResolver(SystemFontCatalog catalog, SKFontManager? manager = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
        this.manager = manager ?? SKFontManager.Default;
    }

    internal ResolvedSystemFont Resolve(SubtitleStyle style)
    {
        if (style.FontVariant is not { } variant)
        {
            using var fontStyle = FontStyle(style);
            return new(SKTypeface.FromFamilyName(style.FontFamily, fontStyle), null, false);
        }
        var exact = Match(style.FontFamily, variant);
        if (exact is not null && TryOpen(exact) is { } resolved)
        {
            return resolved with { IsExactMatch = true };
        }
        var familyFaces = catalog.Faces.Where(face => MatchesFamily(face, style.FontFamily));
        foreach (var candidate in familyFaces.OrderBy(face => face.Variant.Italic != style.Italic)
                     .ThenBy(face => Math.Abs(face.Variant.Width - variant.Width))
                     .ThenBy(face => Math.Abs(face.Variant.Weight - variant.Weight))
                     .ThenBy(face => face.IsVariable))
        {
            if (candidate == exact)
            {
                continue;
            }
            if (TryOpen(candidate) is { } fallback)
            {
                return fallback;
            }
        }
        using var requestedStyle = FontStyle(style);
        var typeface = manager.MatchFamily(style.FontFamily, requestedStyle)
            ?? SKTypeface.FromFamilyName(style.FontFamily, requestedStyle)
            ?? SKTypeface.CreateDefault();
        return new(typeface, Describe(typeface), false);
    }

    internal SystemFontFace? Match(string familyName, SubtitleFontVariant variant)
    {
        if (variant.PostScriptName is { } postScriptName)
        {
            var byPostScript = catalog.Faces.FirstOrDefault(face =>
                string.Equals(face.Variant.PostScriptName, postScriptName, StringComparison.OrdinalIgnoreCase));
            if (byPostScript is not null)
            {
                return byPostScript;
            }
        }
        return catalog.Faces.FirstOrDefault(face => MatchesFamily(face, familyName)
            && NormalizeName(face.Variant.Name) == NormalizeName(variant.Name)
            && face.Variant.Weight == variant.Weight && face.Variant.Width == variant.Width && face.Variant.Italic == variant.Italic);
    }

    internal static SKFontStyle FontStyle(SubtitleStyle style)
    {
        return new(style.FontVariant?.Weight ?? (style.Bold ? 700 : 400), style.FontVariant?.Width ?? 5,
            style.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright);
    }

    internal static SystemFontFace? Describe(SKTypeface typeface)
    {
        try
        {
            var faces = SystemFontCatalog.ReadTypeface(typeface, typeface.FamilyName, 0, null);
            var enumerated = faces.FirstOrDefault(face => face.IsStyleEnumerated);
            if (enumerated is not null || typeface.PostScriptName is not { } postScriptName)
            {
                return enumerated;
            }
            return faces.FirstOrDefault(face => face.Variant.PostScriptName is { } candidateName
                && NormalizeName(candidateName) == NormalizeName(postScriptName));
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private ResolvedSystemFont? TryOpen(SystemFontFace candidate)
    {
        using var styles = manager.GetFontStyles(candidate.SourceFamilyName);
        if (candidate.StyleIndex < 0 || candidate.StyleIndex >= styles.Count)
        {
            return null;
        }
        var typeface = styles.CreateTypeface(candidate.StyleIndex);
        if (typeface is null)
        {
            return null;
        }
        if (candidate.IsStyleEnumerated)
        {
            try
            {
                var nativeFaces = SystemFontCatalog.ReadTypeface(typeface, candidate.SourceFamilyName, candidate.StyleIndex,
                    styles.GetStyleName(candidate.StyleIndex));
                var actual = nativeFaces.FirstOrDefault(face => face.IsStyleEnumerated
                    && face.Variant.Weight == candidate.Variant.Weight && face.Variant.Width == candidate.Variant.Width
                    && face.Variant.Italic == candidate.Variant.Italic
                    && NormalizeName(face.Variant.Name) == NormalizeName(candidate.Variant.Name));
                if (actual is not null)
                {
                    return new(typeface, actual, false);
                }
            }
            catch (InvalidDataException)
            {
            }
            typeface.Dispose();
            return null;
        }
        if (!candidate.IsVariable)
        {
            typeface.Dispose();
            return null;
        }
        using (typeface)
        using (var stream = typeface.OpenStream())
        {
            if (stream is null)
            {
                return null;
            }
            using var data = SKData.Create(stream);
            var namedIndex = checked((candidate.NamedInstanceIndex << 16) | candidate.CollectionIndex);
            var named = SKTypeface.FromData(data, namedIndex);
            if (named is null)
            {
                return null;
            }
            using var namedStream = named.OpenStream(out var actualIndex);
            if (namedStream is not null && actualIndex == namedIndex)
            {
                return new(named, candidate, false);
            }
            named.Dispose();
            return null;
        }
    }

    private static bool MatchesFamily(SystemFontFace face, string familyName)
    {
        return string.Equals(face.FamilyName, familyName, StringComparison.OrdinalIgnoreCase)
            || face.Aliases.Contains(familyName, StringComparer.OrdinalIgnoreCase);
    }

    private static string NormalizeName(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }
}
