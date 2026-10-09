using SkiaSharp;

namespace AegiNext.Rendering.Tests;

/// <summary>需要本机 Noto 中文命名变种，用于菜单预览的真实字体后端验收。</summary>
public sealed class MacFontNamePreviewFactAttribute : FactAttribute
{
    /// <summary>缺少验收字体时显式跳过，夹具覆盖不替代本机命名变种的绘制证据。</summary>
    public MacFontNamePreviewFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "Requires macOS and installed Noto Chinese font variants.";
            return;
        }

        var installed = SKFontManager.Default.GetFontFamilies().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!installed.IsSupersetOf(["Noto Sans SC", "Noto Serif SC"]))
        {
            Skip = "Requires installed Noto Sans SC and Noto Serif SC fonts.";
        }
    }
}
