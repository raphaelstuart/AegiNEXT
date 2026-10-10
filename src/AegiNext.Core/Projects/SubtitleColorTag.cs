namespace AegiNext.Core.Projects;

/// <summary>字幕分类标记的稳定身份、名称和不透明 sRGB 颜色；工程保存使用时的独立定义。</summary>
public sealed record SubtitleColorTag
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = string.Empty;
    public string ColorHex { get; init; } = "#000000";
}
