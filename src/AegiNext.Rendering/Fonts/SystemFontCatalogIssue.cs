namespace AegiNext.Rendering.Fonts;

/// <summary>系统字体枚举中无法解析的字体表，其他候选仍然可用。</summary>
public sealed record SystemFontCatalogIssue(string FamilyName, int StyleIndex, string Message);
