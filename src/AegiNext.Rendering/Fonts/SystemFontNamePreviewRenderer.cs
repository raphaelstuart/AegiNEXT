using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Fonts;

/// <summary>通过系统字体目录和字幕渲染共用的解析器取得真实静态 face 或可变字体命名实例。</summary>
public sealed class SystemFontNamePreviewRenderer : IFontNamePreviewRenderer
{
    private readonly Func<SystemFontCatalog?> catalog;

    /// <summary>借用应用持有的当前目录快照，不在构造时枚举字体或打开字体资源。</summary>
    public SystemFontNamePreviewRenderer(Func<SystemFontCatalog?> catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        this.catalog = catalog;
    }

    /// <inheritdoc />
    public IFontNamePreviewFace? Resolve(FontNamePreviewRequest request, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        request.Validate();
        var snapshot = catalog();
        if (snapshot is null)
        {
            return null;
        }

        var resolver = new SystemFontResolver(snapshot);
        var candidate = request.Variant is { } variant
            ? resolver.Match(request.FamilyName, variant)
            : snapshot.Faces.Where(face =>
                    string.Equals(face.FamilyName, request.FamilyName, StringComparison.OrdinalIgnoreCase) ||
                    face.Aliases.Contains(request.FamilyName, StringComparer.OrdinalIgnoreCase))
                .OrderBy(face => face.Variant.Italic)
                .ThenBy(face => Math.Abs(face.Variant.Width - 5))
                .ThenBy(face => Math.Abs(face.Variant.Weight - 400))
                .ThenBy(face => face.Variant.Name, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        if (candidate is null)
        {
            return null;
        }

        var resolved = resolver.Resolve(new SubtitleStyle
        {
            FontFamily = candidate.FamilyName,
            FontVariant = candidate.Variant,
            Italic = candidate.Variant.Italic
        });
        if (!resolved.IsExactMatch || resolved.Face is null)
        {
            resolved.Typeface.Dispose();
            return null;
        }

        return SystemFontNamePreviewFace.TryCreate(resolved.Typeface, resolved.Face, token);
    }
}
