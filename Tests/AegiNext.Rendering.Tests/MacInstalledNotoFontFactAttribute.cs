using SkiaSharp;

namespace AegiNext.Rendering.Tests;

/// <summary>仅在安装了当前验收所需 Noto 静态及可变字体组合的 macOS 上运行。</summary>
public sealed class MacInstalledNotoFontFactAttribute : FactAttribute
{
    /// <summary>缺少本机 Noto 字体组合时明确跳过，不把纯夹具测试当作系统字体证据。</summary>
    public MacInstalledNotoFontFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "Requires macOS and the installed Noto font acceptance set.";
            return;
        }
        var manager = SKFontManager.Default;
        var installed = manager.GetFontFamilies().ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (!installed.IsSupersetOf(["Noto Sans SC", "Noto Sans", "Noto Serif SC"]))
        {
            Skip = "Requires installed Noto Sans SC, Noto Sans and Noto Serif SC fonts.";
            return;
        }
        var variable = false;
        var independent = false;
        using var styles = manager.GetFontStyles("Noto Sans SC");
        for (var index = 0; index < styles.Count; index++)
        {
            using var typeface = styles.CreateTypeface(index);
            if (typeface is null)
            {
                continue;
            }
            var isVariable = typeface.GetTableSize(0x66766172) > 0;
            variable |= isVariable;
            independent |= !isVariable;
        }
        if (!variable || !independent)
        {
            Skip = "Requires both independent and variable Noto Sans SC fonts.";
        }
    }
}
