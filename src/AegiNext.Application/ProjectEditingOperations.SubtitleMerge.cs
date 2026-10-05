using AegiNext.Core.Projects;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>按同轨时间顺序原子准备多句合并，保留首句身份与样式，并沿用无损合并限制。</summary>
    public static ProjectDocument MergeSubtitles(ProjectDocument document, IEnumerable<Guid> subtitleIds, string separator = "\n")
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(subtitleIds);
        var ids = subtitleIds.ToArray();
        if (ids.Length < 2 || ids.Distinct().Count() != ids.Length)
        {
            throw new ArgumentException("合并必须选择至少两条不同的字幕。", nameof(subtitleIds));
        }

        var lines = ids.Select(id => document.Subtitles[SubtitleIndex(document, id)]).OrderBy(line => line.Start).ToArray();
        var prepared = document;
        foreach (var line in lines.Skip(1))
        {
            prepared = MergeSubtitles(prepared, lines[0].Id, line.Id, separator);
        }

        return prepared;
    }
}
