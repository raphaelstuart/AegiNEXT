namespace AegiNext.Core.Projects;

/// <summary>字幕片段的独立时间轨道；数组顺序仅控制编辑器显示，不改变图层合成顺序。</summary>
public sealed record SubtitleTrack
{
    public static Guid DEFAULT_TRACK_ID { get; } = new("7ae81f88-e3d5-4f36-baaa-2a46c25c0841");
    public static SubtitleTrack Default { get; } = new() { Id = DEFAULT_TRACK_ID, Name = "Subtitles" };
    public Guid Id { get; init; } = Guid.NewGuid();
    public string Name { get; init; } = "Subtitles";
    public SubtitleStyle? DefaultStyle { get; init; }
    public Guid? StylePresetId { get; init; }
    public string? StylePresetName { get; init; }
}
