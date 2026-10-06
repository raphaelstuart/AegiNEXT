namespace AegiNext.Rendering.Tests;

/// <summary>使用 macOS 随系统提供的中文及 emoji 字体验证真实系统回退。</summary>
public sealed class MacSystemFontFactAttribute : FactAttribute
{
    /// <summary>其他平台保留显式跳过状态，不把缺失系统字体当作已验证。</summary>
    public MacSystemFontFactAttribute()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Skip = "Requires macOS system CJK and emoji fonts.";
        }
    }
}
