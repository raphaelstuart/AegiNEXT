using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>字体字段的完整语义值，区分普通家族与系统命名变体。</summary>
public readonly record struct FontSelection
{
    /// <summary>创建家族或命名变体选择；系统选择由宿主提供的目录确认。</summary>
    public FontSelection(string familyName, SubtitleFontVariant? variant = null, bool isSystemFont = false)
    {
        ArgumentNullException.ThrowIfNull(familyName);
        FamilyName = familyName;
        Variant = variant;
        IsSystemFont = isSystemFont;
    }

    public string FamilyName { get; } = string.Empty;
    public SubtitleFontVariant? Variant { get; }
    public bool IsSystemFont { get; }
    public string DisplayName => Variant is { Name.Length: > 0 } variant ? $"{FamilyName} {variant.Name}" : FamilyName;
}
