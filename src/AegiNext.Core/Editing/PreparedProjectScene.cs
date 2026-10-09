using System.Collections.Frozen;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Editing;

/// <summary>只验证一次的不可变渲染快照；重复帧求值复用字幕索引而不重复验证全文。</summary>
public sealed class PreparedProjectScene
{
    /// <summary>验证工程并准备只读索引；工程快照变化后创建新实例。</summary>
    public PreparedProjectScene(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        Document = document;
        Subtitles = document.Subtitles.ToFrozenDictionary(line => line.Id);
        Clips = new(document);
    }

    public ProjectDocument Document { get; }
    public ProjectClipIndex Clips { get; }
    internal FrozenDictionary<Guid, SubtitleLine> Subtitles { get; }
}
