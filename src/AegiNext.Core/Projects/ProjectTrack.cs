namespace AegiNext.Core.Projects;

/// <summary>容纳字幕、图片和形状片段的独立轨道；工程数组中越靠前的轨道显示在越上层。</summary>
public sealed record ProjectTrack
{
    public static Guid DEFAULT_TRACK_ID { get; } = new("7ae81f88-e3d5-4f36-baaa-2a46c25c0841");
    public static ProjectTrack Default { get; } = new() { Id = DEFAULT_TRACK_ID, Name = "Track" };
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Track";
    public SubtitleStyle? DefaultStyle { get; init; }
    public Guid? StylePresetId { get; init; }
    public string? StylePresetName { get; init; }
    public bool AutoApplyStyle { get; init; } = true;
}
