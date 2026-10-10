using System.Collections.Immutable;
using AegiNext.Core.Projects;

namespace AegiNext.Application;

public static partial class ProjectEditingOperations
{
    /// <summary>一次设置或清除指定字幕的工程颜色标记，不改动关联片段或动画。</summary>
    public static ProjectDocument SetSubtitleColorTag(ProjectDocument document, IReadOnlyCollection<Guid> subtitleIds, Guid? tagId)
    {
        ProjectValidator.Validate(document);
        var selection = SelectColorTagSubtitles(document, subtitleIds);
        if (tagId is { } identity && !document.ColorTags.Any(tag => tag.Id == identity))
        {
            throw new KeyNotFoundException("工程颜色标记不存在。");
        }
        return SetSubtitleColorTagCore(document, selection, tagId);
    }

    /// <summary>按名称和 sRGB 色值复用工程定义，或导入独立定义并一次赋给全部选中字幕。</summary>
    public static ProjectDocument ApplySubtitleColorTag(ProjectDocument document, IReadOnlyCollection<Guid> subtitleIds, SubtitleColorTag template)
    {
        ProjectValidator.Validate(document);
        SubtitleColorTagValidator.Validate(template);
        var selection = SelectColorTagSubtitles(document, subtitleIds);
        if (selection.Count == 0)
        {
            return document;
        }
        var tag = document.ColorTags.FirstOrDefault(existing => SameColorTagDefinition(existing, template));
        if (tag is null)
        {
            tag = template with { Id = NewColorTagId(document.ColorTags.ToBuilder()), ColorHex = template.ColorHex.ToUpperInvariant() };
            document = document with { ColorTags = document.ColorTags.Add(tag) };
        }
        return SetSubtitleColorTagCore(document, selection, tag.Id);
    }

    private static HashSet<Guid> SelectColorTagSubtitles(ProjectDocument document, IReadOnlyCollection<Guid> subtitleIds)
    {
        ArgumentNullException.ThrowIfNull(subtitleIds);
        var selection = subtitleIds.ToHashSet();
        if (document.Subtitles.Count(line => selection.Contains(line.Id)) != selection.Count)
        {
            throw new KeyNotFoundException("所选字幕不存在。");
        }
        return selection;
    }

    private static ProjectDocument SetSubtitleColorTagCore(ProjectDocument document, HashSet<Guid> selection, Guid? tagId)
    {
        ImmutableArray<SubtitleLine>.Builder? changed = null;
        for (var index = 0; index < document.Subtitles.Length; index++)
        {
            var line = document.Subtitles[index];
            if (selection.Contains(line.Id) && line.ColorTagId != tagId)
            {
                changed ??= document.Subtitles.ToBuilder();
                changed[index] = line with { ColorTagId = tagId };
            }
        }
        return changed is null ? document : Verified(document with { Subtitles = changed.ToImmutable() });
    }

    private static bool SameColorTagDefinition(SubtitleColorTag first, SubtitleColorTag second)
    {
        return string.Equals(first.Name, second.Name, StringComparison.Ordinal) &&
            string.Equals(first.ColorHex, second.ColorHex, StringComparison.OrdinalIgnoreCase);
    }

    private static Dictionary<Guid, Guid> ImportColorTags(ImmutableArray<SubtitleColorTag>.Builder target,
        ImmutableArray<SubtitleColorTag> incoming, HashSet<Guid>? reservedIds = null)
    {
        SubtitleColorTagValidator.Validate(incoming);
        var mapping = new Dictionary<Guid, Guid>();
        var definitions = new Dictionary<(string Name, string ColorHex), SubtitleColorTag>();
        var identities = new HashSet<Guid>();
        foreach (var tag in target)
        {
            definitions.TryAdd((tag.Name, tag.ColorHex.ToUpperInvariant()), tag);
            identities.Add(tag.Id);
        }
        foreach (var source in incoming)
        {
            var key = (source.Name, source.ColorHex.ToUpperInvariant());
            if (definitions.TryGetValue(key, out var existing))
            {
                mapping.Add(source.Id, existing.Id);
                continue;
            }
            var id = source.Id;
            if (identities.Contains(id))
            {
                id = reservedIds is not null ? NewMergeId(reservedIds) : NewColorTagId(target);
            }
            var imported = source with { Id = id, ColorHex = key.Item2 };
            target.Add(imported);
            definitions.Add(key, imported);
            identities.Add(id);
            mapping.Add(source.Id, id);
        }
        if (target.Count > SubtitleColorTagValidator.MAXIMUM_TAGS)
        {
            throw new InvalidDataException("字幕颜色标记超过数量预算。");
        }
        return mapping;
    }

    private static Guid NewColorTagId(ImmutableArray<SubtitleColorTag>.Builder tags)
    {
        Guid id;
        do
        {
            id = Guid.NewGuid();
        }
        while (tags.Any(tag => tag.Id == id));
        return id;
    }
}
