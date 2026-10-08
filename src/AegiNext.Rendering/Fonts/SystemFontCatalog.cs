using System.Collections.Immutable;
using AegiNext.Core.Projects;
using SkiaSharp;

namespace AegiNext.Rendering.Fonts;

/// <summary>系统已安装字体的托管快照，不保留字体或字体样式集合的原生资源。</summary>
public sealed class SystemFontCatalog
{
    /// <summary>枚举默认系统字体管理器中的全部字体。</summary>
    public SystemFontCatalog() : this(SKFontManager.Default)
    {
    }

    /// <summary>枚举指定字体管理器及可选的家族集合，管理器的生命周期由调用方持有。</summary>
    public SystemFontCatalog(SKFontManager manager, IEnumerable<string>? familyNames = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manager);
        var faces = ImmutableArray.CreateBuilder<SystemFontFace>();
        var issues = ImmutableArray.CreateBuilder<SystemFontCatalogIssue>();
        foreach (var family in (familyNames ?? manager.GetFontFamilies()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(family))
            {
                continue;
            }
            using var styles = manager.GetFontStyles(family);
            for (var index = 0; index < styles.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var typeface = styles.CreateTypeface(index);
                if (typeface is null)
                {
                    issues.Add(new(family, index, "The font manager did not return a typeface."));
                    continue;
                }
                try
                {
                    faces.AddRange(ReadTypeface(typeface, family, index, styles.GetStyleName(index)));
                }
                catch (InvalidDataException exception)
                {
                    issues.Add(new(family, index, exception.Message));
                }
            }
        }
        Faces = Merge(faces);
        Issues = issues.ToImmutable();
    }

    internal SystemFontCatalog(IEnumerable<SystemFontFace> faces)
    {
        Faces = Merge(faces);
        Issues = [];
    }

    /// <summary>无需枚举原生字体即可取得空目录。</summary>
    public static SystemFontCatalog Empty { get; } = new(Array.Empty<SystemFontFace>());

    public ImmutableArray<SystemFontFace> Faces { get; }
    public ImmutableArray<SystemFontCatalogIssue> Issues { get; }

    internal static ImmutableArray<SystemFontFace> ReadTypeface(SKTypeface typeface, string sourceFamilyName, int styleIndex, string? styleName)
    {
        var metadata = OpenTypeFontMetadataReader.Read(typeface);
        using var stream = typeface.OpenStream(out var fontIndex);
        return CreateFaces(metadata, new()
        {
            FamilyName = typeface.FamilyName, SourceFamilyName = sourceFamilyName, StyleIndex = styleIndex,
            StyleName = styleName, PostScriptName = typeface.PostScriptName,
            Weight = typeface.FontWeight, Width = typeface.FontWidth, Italic = typeface.IsItalic,
            FontIndex = stream is null ? 0 : fontIndex
        });
    }

    internal static ImmutableArray<SystemFontFace> CreateFaces(OpenTypeFontMetadata metadata, SystemFontStyleSource source)
    {
        if (source.PostScriptName is { Length: 0 })
        {
            source = source with { PostScriptName = null };
        }
        if (!string.IsNullOrEmpty(source.FamilyName))
        {
            OpenTypeFontMetadataReader.ValidateName(source.FamilyName);
        }
        var familyName = metadata.FamilyName ?? (string.IsNullOrWhiteSpace(source.FamilyName) ? source.SourceFamilyName : source.FamilyName);
        var aliases = metadata.FamilyAliases.Append(source.SourceFamilyName).Append(source.FamilyName).Append(familyName)
            .Where(alias => !string.IsNullOrWhiteSpace(alias)).Distinct(StringComparer.OrdinalIgnoreCase).ToImmutableArray();
        foreach (var alias in aliases)
        {
            OpenTypeFontMetadataReader.ValidateName(alias);
        }
        if (source.PostScriptName is not null)
        {
            OpenTypeFontMetadataReader.ValidateName(source.PostScriptName);
        }
        if (!string.IsNullOrEmpty(source.StyleName))
        {
            OpenTypeFontMetadataReader.ValidateName(source.StyleName);
        }
        var collectionIndex = source.FontIndex & 0xFFFF;
        var isVariable = !metadata.Axes.IsEmpty;
        var template = new SystemFontFace
        {
            FamilyName = familyName,
            Aliases = aliases,
            SourceFamilyName = source.SourceFamilyName,
            StyleIndex = source.StyleIndex,
            CollectionIndex = collectionIndex,
            IsVariable = isVariable,
            IsStyleEnumerated = true
        };
        if (!isVariable)
        {
            return [template with
            {
                Variant = new()
                {
                    Name = metadata.SubfamilyName ?? (string.IsNullOrEmpty(source.StyleName) ? FallbackStyleName(metadata, source) : source.StyleName),
                    PostScriptName = source.PostScriptName ?? metadata.PostScriptName,
                    Weight = metadata.Weight ?? Math.Clamp(source.Weight, 1, 1000),
                    Width = metadata.Width ?? Math.Clamp(source.Width, 1, 9),
                    Italic = metadata.Italic ?? source.Italic
                }
            }];
        }
        var matched = FindNativeInstance(metadata, source.PostScriptName, source.StyleName, source.FontIndex >>> 16);
        var result = ImmutableArray.CreateBuilder<SystemFontFace>();
        var hasDefault = false;
        foreach (var instance in metadata.NamedInstances)
        {
            var isDefault = IsDefaultInstance(metadata.Axes, instance.Coordinates);
            hasDefault |= isDefault;
            var enumerated = matched?.Index == instance.Index;
            result.Add(template with
            {
                NamedInstanceIndex = instance.Index,
                IsStyleEnumerated = enumerated,
                Variant = Variant(metadata, instance.Name,
                    instance.PostScriptName ?? (enumerated ? source.PostScriptName : isDefault ? metadata.PostScriptName : null), instance.Coordinates)
            });
        }
        if (!hasDefault)
        {
            var coordinates = metadata.Axes.ToImmutableDictionary(axis => axis.Tag, axis => axis.Default);
            result.Add(template with
            {
                IsStyleEnumerated = matched is null,
                Variant = Variant(metadata, metadata.SubfamilyName ?? (string.IsNullOrEmpty(source.StyleName) ? FallbackStyleName(metadata, source) : source.StyleName), metadata.PostScriptName, coordinates)
            });
        }
        return result.ToImmutable();
    }

    private static string FallbackStyleName(OpenTypeFontMetadata metadata, SystemFontStyleSource source)
    {
        var weightAxis = metadata.Axes.FirstOrDefault(axis => axis.Tag == OpenTypeFontMetadataReader.WEIGHT_TAG);
        var widthAxis = metadata.Axes.FirstOrDefault(axis => axis.Tag == OpenTypeFontMetadataReader.WIDTH_TAG);
        var italicAxis = metadata.Axes.FirstOrDefault(axis => axis.Tag == OpenTypeFontMetadataReader.ITALIC_TAG);
        var slantAxis = metadata.Axes.FirstOrDefault(axis => axis.Tag == OpenTypeFontMetadataReader.SLANT_TAG);
        var weight = weightAxis is not null ? (int)MathF.Round(weightAxis.Default) : metadata.Weight ?? Math.Clamp(source.Weight, 1, 1000);
        var width = widthAxis is not null ? WidthClass(widthAxis.Default) : metadata.Width ?? Math.Clamp(source.Width, 1, 9);
        var italic = italicAxis is not null ? italicAxis.Default != 0 : slantAxis is not null ? slantAxis.Default != 0 : metadata.Italic ?? source.Italic;
        var name = weight == 400 ? "Regular" : $"Weight {weight}";
        if (width != 5)
        {
            name += $" Width {width}";
        }
        if (italic)
        {
            name += " Italic";
        }
        return name;
    }

    private static OpenTypeNamedInstance? FindNativeInstance(OpenTypeFontMetadata metadata, string? postScriptName, string? styleName, int streamInstanceIndex)
    {
        var streamInstance = metadata.NamedInstances.FirstOrDefault(instance => instance.Index == streamInstanceIndex);
        if (streamInstance is not null)
        {
            return streamInstance;
        }
        var postScriptInstance = string.IsNullOrWhiteSpace(postScriptName)
            ? null
            : metadata.NamedInstances.FirstOrDefault(instance => string.Equals(instance.PostScriptName, postScriptName, StringComparison.OrdinalIgnoreCase));
        var namedStyle = string.IsNullOrWhiteSpace(styleName)
            ? null
            : metadata.NamedInstances.FirstOrDefault(instance => NormalizeName(instance.Name) == NormalizeName(styleName));
        if (namedStyle is not null && postScriptInstance is not null && namedStyle.Index != postScriptInstance.Index
            && string.Equals(postScriptName, metadata.PostScriptName, StringComparison.OrdinalIgnoreCase))
        {
            return namedStyle;
        }
        return postScriptInstance ?? namedStyle;
    }

    private static SubtitleFontVariant Variant(OpenTypeFontMetadata metadata, string name, string? postScriptName, ImmutableDictionary<uint, float> coordinates)
    {
        var weight = coordinates.TryGetValue(OpenTypeFontMetadataReader.WEIGHT_TAG, out var weightCoordinate)
            ? (int)MathF.Round(weightCoordinate) : metadata.Weight ?? 400;
        if (weight is < 1 or > 1000)
        {
            throw new InvalidDataException("OpenType weight coordinate is outside the supported style range.");
        }
        var width = coordinates.TryGetValue(OpenTypeFontMetadataReader.WIDTH_TAG, out var widthCoordinate)
            ? WidthClass(widthCoordinate) : metadata.Width ?? 5;
        var italic = coordinates.TryGetValue(OpenTypeFontMetadataReader.ITALIC_TAG, out var italicCoordinate)
            ? italicCoordinate != 0
            : coordinates.TryGetValue(OpenTypeFontMetadataReader.SLANT_TAG, out var slantCoordinate)
                ? slantCoordinate != 0 : metadata.Italic ?? false;
        return new() { Name = name, PostScriptName = postScriptName, Weight = weight, Width = width, Italic = italic };
    }

    private static int WidthClass(float percentage)
    {
        ReadOnlySpan<float> widths = [50, 62.5f, 75, 87.5f, 100, 112.5f, 125, 150, 200];
        var closest = 0;
        for (var index = 1; index < widths.Length; index++)
        {
            if (Math.Abs(widths[index] - percentage) < Math.Abs(widths[closest] - percentage))
            {
                closest = index;
            }
        }
        return closest + 1;
    }

    private static bool IsDefaultInstance(ImmutableArray<OpenTypeVariationAxis> axes, ImmutableDictionary<uint, float> coordinates)
    {
        return axes.All(axis => coordinates[axis.Tag] == axis.Default);
    }

    private static ImmutableArray<SystemFontFace> Merge(IEnumerable<SystemFontFace> faces)
    {
        var families = faces.GroupBy(face => face.FamilyName, StringComparer.OrdinalIgnoreCase);
        var result = ImmutableArray.CreateBuilder<SystemFontFace>();
        foreach (var family in families)
        {
            var aliases = family.SelectMany(face => face.Aliases).Append(family.Key)
                .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToImmutableArray();
            var variants = family.GroupBy(face => (NormalizeName(face.Variant.Name), face.Variant.Weight, face.Variant.Width, face.Variant.Italic));
            foreach (var variantsGroup in variants)
            {
                var preferred = variantsGroup.OrderBy(face => face.IsVariable).ThenByDescending(face => face.IsStyleEnumerated)
                    .ThenBy(face => face.SourceFamilyName, StringComparer.Ordinal).ThenBy(face => face.StyleIndex).First();
                result.Add(preferred with { FamilyName = family.Key, Aliases = aliases });
            }
        }
        return result.OrderBy(face => face.FamilyName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(face => face.Variant.Weight).ThenBy(face => face.Variant.Width).ThenBy(face => face.Variant.Italic)
            .ThenBy(face => face.Variant.Name, StringComparer.OrdinalIgnoreCase).ToImmutableArray();
    }

    private static string NormalizeName(string value)
    {
        return string.Concat(value.Where(char.IsLetterOrDigit)).ToUpperInvariant();
    }
}
