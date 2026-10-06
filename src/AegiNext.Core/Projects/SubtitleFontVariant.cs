using System.Text.Json.Serialization;

namespace AegiNext.Core.Projects;

/// <summary>系统字体命名变体的身份及设计样式特征，不包含平台字体对象或字体文件路径。</summary>
public readonly record struct SubtitleFontVariant
{
    /// <summary>创建字重 400、标准宽度的变体；保存前必须设置实际变体名称。</summary>
    public SubtitleFontVariant()
    {
    }

    public string Name { get; init; } = string.Empty;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PostScriptName { get; init; }
    public int Weight { get; init; } = 400;
    public int Width { get; init; } = 5;
    public bool Italic { get; init; }
}
