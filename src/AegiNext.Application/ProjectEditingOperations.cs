using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

/// <summary>字幕拆合及合成树整理的纯不可变事务，可直接传给 ProjectEditor.Apply。</summary>
public static partial class ProjectEditingOperations
{
    /// <summary>在严格内部播放头与字素边界拆分；左句保留标识，右句新建标识并延续原动画相位。</summary>
    public static ProjectDocument SplitSubtitle(ProjectDocument document, Guid subtitleId, MediaTime playhead, int utf16Offset)
    {
        ProjectValidator.Validate(document);
        var index = SubtitleIndex(document, subtitleId);
        var original = document.Subtitles[index];
        if (playhead <= original.Start || playhead >= original.End || utf16Offset <= 0 || utf16Offset >= original.Text.Length ||
            !StringInfo.ParseCombiningCharacters(original.Text).Contains(utf16Offset))
        {
            throw new ArgumentException("拆分必须位于字幕时间内部和完整字素边界。", nameof(utf16Offset));
        }

        var leftText = original.Text[..utf16Offset];
        var rightText = original.Text[utf16Offset..];
        if (string.IsNullOrWhiteSpace(leftText) || string.IsNullOrWhiteSpace(rightText))
        {
            throw new ArgumentException("拆分不能产生空白字幕。", nameof(utf16Offset));
        }

        var layer = SubtitleLayer(document.Layers, subtitleId);
        var contentTime = playhead - original.Start + layer.AnimationOffset;
        var leftKaraoke = ImmutableArray.CreateBuilder<KaraokeSegment>();
        var rightKaraoke = ImmutableArray.CreateBuilder<KaraokeSegment>();
        foreach (var segment in original.Karaoke)
        {
            var end = segment.Utf16Start + segment.Utf16Length;
            if (end <= utf16Offset)
            {
                leftKaraoke.Add(segment);
            }
            else if (segment.Utf16Start >= utf16Offset)
            {
                rightKaraoke.Add(segment with { Utf16Start = segment.Utf16Start - utf16Offset });
            }
            else
            {
                if (contentTime <= segment.Start || contentTime >= segment.End)
                {
                    throw new InvalidOperationException("跨字词卡拉 OK 的拆分时刻必须在该片段的高亮区间内部。");
                }

                leftKaraoke.Add(segment with { Utf16Length = utf16Offset - segment.Utf16Start, End = contentTime });
                rightKaraoke.Add(segment with { Utf16Start = 0, Utf16Length = end - utf16Offset, Start = contentTime });
            }
        }

        var left = original with { End = playhead, Text = leftText, Karaoke = leftKaraoke.ToImmutable() };
        var right = original with { Id = Guid.NewGuid(), Start = playhead, Text = rightText, Karaoke = rightKaraoke.ToImmutable() };
        var found = false;
        var layers = RewriteSiblings(document.Layers, layer.Id, (siblings, layerIndex) => siblings
            .SetItem(layerIndex, LayerAnimationTiming.Clip(layer with { End = playhead }))
            .Insert(layerIndex + 1, LayerAnimationTiming.Clip(layer with
            {
                Id = right.Id, SubtitleId = right.Id, Start = playhead,
                AnimationOffset = contentTime
            })), ref found);
        return Verified(document with { Subtitles = document.Subtitles.SetItem(index, left).Insert(index + 1, right), Layers = layers });
    }

    /// <summary>合并相邻且不重叠的字幕，样式取首句；保留高亮完整时钟。不能无损合并的层效果明确拒绝。</summary>
    public static ProjectDocument MergeSubtitles(ProjectDocument document, Guid firstId, Guid secondId, string separator = "\n")
    {
        ProjectValidator.Validate(document);
        ArgumentNullException.ThrowIfNull(separator);
        ProjectValidator.ValidateText(separator);
        var firstIndex = SubtitleIndex(document, firstId);
        var secondIndex = SubtitleIndex(document, secondId);
        var first = document.Subtitles[firstIndex];
        var second = document.Subtitles[secondIndex];
        var next = document.Subtitles.Where(line => line.TrackId == first.TrackId && line.Start > first.Start)
            .OrderBy(line => line.Start).FirstOrDefault();
        if (first.TrackId != second.TrackId || next?.Id != secondId)
        {
            throw new InvalidOperationException("只能合并同一字幕轨道按时间相邻的两句。");
        }
        if (first.End > second.Start)
        {
            throw new InvalidOperationException("重叠字幕不能合并为单个连续句。");
        }

        var firstLayer = SubtitleLayer(document.Layers, firstId);
        var secondLayer = SubtitleLayer(document.Layers, secondId);
        if (!HasNeutralVisuals(firstLayer) || !HasNeutralVisuals(secondLayer))
        {
            throw new InvalidOperationException("包含图层动画、变换、蒙版或混合效果的字幕不能无损合并，请先保留为组。");
        }

        var firstOrigin = first.Start - firstLayer.AnimationOffset;
        var secondOrigin = second.Start - secondLayer.AnimationOffset;
        var mergedOrigin = firstOrigin < secondOrigin ? firstOrigin : secondOrigin;
        var mergedOffset = first.Start - mergedOrigin;
        var firstKaraoke = RebaseKaraoke(first, firstLayer, mergedOrigin, 0);
        var secondKaraoke = RebaseKaraoke(second, secondLayer, mergedOrigin,
            checked(first.Text.Length + separator.Length));
        var merged = first with
        {
            End = second.End,
            Text = first.Text + separator + second.Text,
            Karaoke = firstKaraoke.AddRange(secondKaraoke)
        };
        var found = false;
        var layers = RewriteSiblings(document.Layers, firstLayer.Id, (siblings, layerIndex) =>
        {
            if (layerIndex + 1 >= siblings.Length || siblings[layerIndex + 1].Id != secondLayer.Id)
            {
                throw new InvalidOperationException("字幕层必须是相邻同级节点，以免改变与其他图层的遮挡顺序。");
            }

            return siblings.SetItem(layerIndex, firstLayer with { End = second.End, AnimationOffset = mergedOffset }).RemoveAt(layerIndex + 1);
        }, ref found);
        return Verified(document with { Subtitles = document.Subtitles.SetItem(firstIndex, merged).RemoveAt(secondIndex), Layers = layers });
    }

    /// <summary>递归删除节点及其子树，并同步删除相应字幕行。</summary>
    public static ProjectDocument RemoveLayer(ProjectDocument document, Guid layerId)
    {
        ProjectValidator.Validate(document);
        var target = FindLayer(document.Layers, layerId) ?? throw new KeyNotFoundException("图层不存在。");
        var removedSubtitles = Descendants(target).Where(layer => layer.SubtitleId.HasValue).Select(layer => layer.SubtitleId!.Value).ToHashSet();
        var found = false;
        var layers = RewriteSiblings(document.Layers, layerId, (siblings, index) => siblings.RemoveAt(index), ref found);
        return Verified(document with
        {
            Layers = layers,
            Subtitles = document.Subtitles.Where(line => !removedSubtitles.Contains(line.Id)).ToImmutableArray()
        });
    }

    /// <summary>在当前父节点内调整绘制顺序；目标索引是移动后的同级索引。</summary>
    public static ProjectDocument MoveLayer(ProjectDocument document, Guid layerId, int newSiblingIndex)
    {
        ProjectValidator.Validate(document);
        var found = false;
        var layers = RewriteSiblings(document.Layers, layerId, (siblings, index) =>
        {
            ArgumentOutOfRangeException.ThrowIfNegative(newSiblingIndex);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(newSiblingIndex, siblings.Length);
            return index == newSiblingIndex ? siblings : siblings.RemoveAt(index).Insert(newSiblingIndex, siblings[index]);
        }, ref found);
        if (!found)
        {
            throw new KeyNotFoundException("图层不存在。");
        }

        return layers == document.Layers ? document : Verified(document with { Layers = layers });
    }

    /// <summary>将同父连续节点分组；只接受 NORMAL 子层，避免隔离合成改变外部混合关系。</summary>
    public static ProjectDocument GroupLayers(ProjectDocument document, ImmutableArray<Guid> layerIds, string name = "Group")
    {
        ProjectValidator.Validate(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (layerIds.IsDefault || layerIds.Length < 2 || layerIds.Distinct().Count() != layerIds.Length)
        {
            throw new ArgumentException("分组至少需要两个不同图层。", nameof(layerIds));
        }

        var selected = layerIds.ToHashSet();
        var found = false;
        var layers = RewriteSiblings(document.Layers, layerIds[0], (siblings, _) =>
        {
            var indices = Enumerable.Range(0, siblings.Length).Where(index => selected.Contains(siblings[index].Id)).ToArray();
            if (indices.Length != selected.Count || indices[^1] - indices[0] + 1 != indices.Length)
            {
                throw new InvalidOperationException("只能分组同父节点下连续的图层。");
            }

            var children = indices.Select(index => siblings[index]).ToImmutableArray();
            if (children.Any(layer => layer.Blend != BlendMode.NORMAL))
            {
                throw new InvalidOperationException("非 NORMAL 混合层分组会改变隔离合成结果，不能隐式重组。");
            }

            var group = new ProjectLayer
            {
                Name = name, Kind = LayerKind.GROUP, Children = children,
                Start = children.Min(layer => layer.Start), End = children.Max(layer => layer.End)
            };
            return siblings.RemoveRange(indices[0], indices.Length).Insert(indices[0], group);
        }, ref found);
        if (!found)
        {
            throw new KeyNotFoundException("图层不存在。");
        }

        return Verified(document with { Layers = layers });
    }

    /// <summary>展开无视觉作用且不裁剪子层的组；复杂组保持原样并明确拒绝有损操作。</summary>
    public static ProjectDocument UngroupLayer(ProjectDocument document, Guid groupId)
    {
        ProjectValidator.Validate(document);
        var found = false;
        var layers = RewriteSiblings(document.Layers, groupId, (siblings, index) =>
        {
            var group = siblings[index];
            if (group.Kind != LayerKind.GROUP || !HasNeutralVisuals(group) ||
                group.Children.Any(child => child.Start < group.Start || child.End > group.End || child.Blend != BlendMode.NORMAL))
            {
                throw new InvalidOperationException("此组包含合成、变换或时间裁剪，不能无损解组。");
            }

            return siblings.RemoveAt(index).InsertRange(index, group.Children);
        }, ref found);
        if (!found)
        {
            throw new KeyNotFoundException("图层不存在。");
        }

        return Verified(document with { Layers = layers });
    }

    private static ImmutableArray<KaraokeSegment> RebaseKaraoke(SubtitleLine line, ProjectLayer layer,
        MediaTime mergedOrigin, int textOffset)
    {
        var result = ImmutableArray.CreateBuilder<KaraokeSegment>();
        var offset = line.Start - layer.AnimationOffset - mergedOrigin;
        foreach (var segment in line.Karaoke)
        {
            result.Add(segment with { Utf16Start = checked(segment.Utf16Start + textOffset), Start = segment.Start + offset, End = segment.End + offset });
        }

        return result.ToImmutable();
    }

    private static bool HasNeutralVisuals(ProjectLayer layer)
    {
        return layer.Transform == new LayerTransform() && layer.Opacity.Equals(1d) && layer.Blend == BlendMode.NORMAL &&
            layer.Blur == 0 && layer.Tracks.IsEmpty && layer.MotionPath is null && layer.Mask is null;
    }

    private static int SubtitleIndex(ProjectDocument document, Guid id)
    {
        for (var index = 0; index < document.Subtitles.Length; index++)
        {
            if (document.Subtitles[index].Id == id)
            {
                return index;
            }
        }

        throw new KeyNotFoundException("字幕不存在。");
    }

    private static ProjectLayer SubtitleLayer(ImmutableArray<ProjectLayer> layers, Guid subtitleId)
    {
        return layers.SelectMany(Descendants).First(layer => layer.SubtitleId == subtitleId);
    }

    private static ProjectLayer? FindLayer(ImmutableArray<ProjectLayer> layers, Guid id)
    {
        return layers.SelectMany(Descendants).FirstOrDefault(layer => layer.Id == id);
    }

    private static IEnumerable<ProjectLayer> Descendants(ProjectLayer layer)
    {
        yield return layer;
        foreach (var child in layer.Children)
        {
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static ImmutableArray<ProjectLayer> RewriteSiblings(ImmutableArray<ProjectLayer> layers, Guid target,
        Func<ImmutableArray<ProjectLayer>, int, ImmutableArray<ProjectLayer>> edit, ref bool found)
    {
        for (var index = 0; index < layers.Length; index++)
        {
            if (layers[index].Id == target)
            {
                found = true;
                return edit(layers, index);
            }

            var children = RewriteSiblings(layers[index].Children, target, edit, ref found);
            if (found)
            {
                return children == layers[index].Children ? layers : layers.SetItem(index, layers[index] with { Children = children });
            }
        }

        return layers;
    }

    private static ProjectDocument Verified(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        return document;
    }
}
