using System.Collections.Immutable;
using System.Numerics;
using AegiNext.Core.Editing;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Application;

/// <summary>不可变快照编辑服务；一次 Apply 对应一个可撤销事务，校验失败不入历史。</summary>
public sealed partial class ProjectEditor
{
    private readonly Lock gate = new();
    private readonly int historyLimit;
    private readonly List<ProjectHistoryEntry> undo = [];
    private readonly List<ProjectHistoryEntry> redo = [];
    private ProjectDocument snapshot;
    private ProjectDocument saved;
    private bool editing;

    /// <summary>创建已验证的工程编辑会话；历史按事务数有界。</summary>
    public ProjectEditor(ProjectDocument? document = null, int historyLimit = 100)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(historyLimit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(historyLimit, 10000);
        snapshot = SubtitleKaraokeNormalization.Normalize(document ?? new());
        saved = snapshot;
        this.historyLimit = historyLimit;
    }

    public event EventHandler? Changed;

    public ProjectDocument Snapshot
    {
        get
        {
            lock (gate)
            {
                return snapshot;
            }
        }
    }
    public bool CanUndo
    {
        get
        {
            lock (gate)
            {
                return undo.Count > 0;
            }
        }
    }
    public bool CanRedo
    {
        get
        {
            lock (gate)
            {
                return redo.Count > 0;
            }
        }
    }
    public bool HasUnsavedChanges
    {
        get
        {
            lock (gate)
            {
                return !ReferenceEquals(snapshot, saved);
            }
        }
    }
    public string? UndoLabel
    {
        get
        {
            lock (gate)
            {
                return undo.Count == 0 ? null : undo[^1].Label;
            }
        }
    }
    public string? RedoLabel
    {
        get
        {
            lock (gate)
            {
                return redo.Count == 0 ? null : redo[^1].Label;
            }
        }
    }

    /// <summary>提交整体快照事务；同一编辑委托不能重入会话。</summary>
    public void Apply(string label, Func<ProjectDocument, ProjectDocument> edit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentNullException.ThrowIfNull(edit);
        lock (gate)
        {
            EnsureNotEditing();
            editing = true;
            try
            {
                var next = edit(snapshot) ?? throw new InvalidOperationException("编辑不能返回空项目。");
                next = SubtitleKaraokeNormalization.Normalize(next);
                if (next == snapshot)
                {
                    return;
                }

                undo.Add(new(label, snapshot, next));
                if (undo.Count > historyLimit)
                {
                    undo.RemoveAt(0);
                }

                redo.Clear();
                snapshot = next;
            }
            finally
            {
                editing = false;
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>撤销最近一个事务；没有历史时返回 false。</summary>
    public bool Undo()
    {
        lock (gate)
        {
            EnsureNotEditing();
            if (undo.Count == 0)
            {
                return false;
            }

            var entry = undo[^1];
            undo.RemoveAt(undo.Count - 1);
            redo.Add(entry);
            snapshot = entry.Before;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>重做最近撤销的事务；分支编辑会清空重做历史。</summary>
    public bool Redo()
    {
        lock (gate)
        {
            EnsureNotEditing();
            if (redo.Count == 0)
            {
                return false;
            }

            var entry = redo[^1];
            redo.RemoveAt(redo.Count - 1);
            undo.Add(entry);
            snapshot = entry.After;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <summary>载入新的已保存工程并清空历史。</summary>
    public void Reset(ProjectDocument document)
    {
        ProjectValidator.Validate(document);
        lock (gate)
        {
            EnsureNotEditing();
            snapshot = document;
            saved = document;
            undo.Clear();
            redo.Clear();
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>标记实际写盘的快照；后台保存旧快照不会将较新的编辑误标为已保存。</summary>
    public void MarkSaved(ProjectDocument? savedSnapshot = null)
    {
        lock (gate)
        {
            EnsureNotEditing();
            saved = savedSnapshot ?? snapshot;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>新增字幕行及同标识字幕层，作为一个事务。</summary>
    public Guid AddSubtitle(MediaTime start, MediaTime end, string text, Guid? trackId = null, SubtitleStyle? fallbackStyle = null)
    {
        var line = new SubtitleLine { Start = start, End = end, Text = text, TrackId = trackId ?? Snapshot.SubtitleTracks[0].Id };
        AddSubtitles([line], line.TrackId, fallbackStyle);
        return line.Id;
    }

    /// <summary>一次性导入字幕行并创建对应合成层。</summary>
    public void AddSubtitles(IEnumerable<SubtitleLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var imported = lines.ToImmutableArray();
        Apply("Import subtitles", document => document with
        {
            Subtitles = document.Subtitles.AddRange(imported),
            Layers = document.Layers.AddRange(imported.Select(CreateSubtitleLayer))
        });
    }

    /// <summary>将整批导入字幕放入指定轨道；任一碰撞或非法项都会拒绝整个导入。</summary>
    public void AddSubtitles(IEnumerable<SubtitleLine> lines, Guid trackId, SubtitleStyle? fallbackStyle = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var imported = lines.ToImmutableArray();
        Apply("Import subtitles", document => ProjectEditingOperations.CreateSubtitleClips(document, imported, trackId, fallbackStyle));
    }

    /// <summary>更新字幕内容或样式；直接修改时间按裁剪语义同步对应层。</summary>
    public void UpdateSubtitle(Guid id, Func<SubtitleLine, SubtitleLine> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        Apply("Edit subtitle", document =>
        {
            var index = FindSubtitle(document, id);
            var before = document.Subtitles[index];
            var after = SubtitleKaraokeNormalization.Normalize(edit(before) ?? throw new InvalidOperationException("字幕不能为空。"));
            if (after.Id != id)
            {
                throw new InvalidOperationException("编辑不能改变字幕标识。");
            }

            if (after == before)
            {
                return document;
            }

            return document with
            {
                Subtitles = document.Subtitles.SetItem(index, after),
                Layers = MapLayers(document.Layers, layer => layer.SubtitleId == id ? LayerAnimationTiming.Clip(layer with
                {
                    Start = after.Start, End = after.End, AnimationOffset = layer.AnimationOffset + after.Start - before.Start
                }) : layer)
            };
        });
    }

    /// <summary>移除字幕及其层，不保留悬空引用。</summary>
    public void RemoveSubtitle(Guid id)
    {
        Apply("Remove subtitle", document => document with
        {
            Subtitles = document.Subtitles.RemoveAt(FindSubtitle(document, id)),
            Layers = RemoveLayers(document.Layers, layer => layer.SubtitleId == id)
        });
    }

    /// <summary>整体移动字幕，保留动画、路径与卡拉 OK 的相对内容时间。</summary>
    public void ShiftSubtitle(Guid id, MediaTime offset)
    {
        Apply("Move subtitle", document =>
        {
            var index = FindSubtitle(document, id);
            if (offset == MediaTime.Zero)
            {
                return document;
            }

            var line = document.Subtitles[index];
            return document with
            {
                Subtitles = document.Subtitles.SetItem(index, line with { Start = line.Start + offset, End = line.End + offset }),
                Layers = MapLayers(document.Layers, layer => layer.SubtitleId == id ? layer with
                {
                    Start = layer.Start + offset, End = layer.End + offset
                } : layer)
            };
        });
    }

    /// <summary>CROP 保持内容相位；STRETCH 精确重定时关键帧、路径、卡拉 OK 和内容偏移。</summary>
    public void SetSubtitleTiming(Guid id, MediaTime start, MediaTime end, TimelineEditMode mode)
    {
        if (start >= end || !Enum.IsDefined(mode))
        {
            throw new ArgumentException("字幕时间或编辑模式无效。", nameof(end));
        }

        Apply(mode == TimelineEditMode.CROP ? "Crop subtitle" : "Stretch subtitle", document =>
        {
            var index = FindSubtitle(document, id);
            var line = document.Subtitles[index];
            if (line.Start == start && line.End == end)
            {
                return document;
            }

            var oldDuration = line.End - line.Start;
            var newDuration = end - start;
            var karaoke = mode == TimelineEditMode.CROP ? line.Karaoke : line.Karaoke.Select(segment => segment with
            {
                Start = Scale(segment.Start, newDuration, oldDuration), End = Scale(segment.End, newDuration, oldDuration)
            }).ToImmutableArray();
            return document with
            {
                Subtitles = document.Subtitles.SetItem(index, line with { Start = start, End = end, Karaoke = karaoke }),
                Layers = MapLayers(document.Layers, layer => layer.SubtitleId != id ? layer : LayerAnimationTiming.Retime(layer, start, end, mode))
            };
        });
    }

    /// <summary>整体移动片段，保留动画的局部内容时间；字幕层同步其字幕行。</summary>
    public void ShiftLayer(Guid id, MediaTime offset)
    {
        var layer = FindLayer(Snapshot.Layers, id);
        if (layer.SubtitleId is { } subtitleId)
        {
            ShiftSubtitle(subtitleId, offset);
            return;
        }

        UpdateLayer(id, value => value with { Start = value.Start + offset, End = value.End + offset });
    }

    /// <summary>按裁剪或拉伸语义修改图层区间；字幕层同步其字幕行。</summary>
    public void SetLayerTiming(Guid id, MediaTime start, MediaTime end, TimelineEditMode mode)
    {
        if (start >= end || !Enum.IsDefined(mode))
        {
            throw new ArgumentException("图层时间或编辑模式无效。", nameof(end));
        }

        var layer = FindLayer(Snapshot.Layers, id);
        if (layer.SubtitleId is { } subtitleId)
        {
            SetSubtitleTiming(subtitleId, start, end, mode);
            return;
        }

        UpdateLayer(id, value => LayerAnimationTiming.Retime(value, start, end, mode));
    }

    /// <summary>添加独立根图层；分组可在一次 Apply 中重组。</summary>
    public void AddLayer(ProjectLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);
        Apply("Add layer", document => document with { Layers = document.Layers.Add(layer) });
    }

    /// <summary>更新任意深度图层，保持稳定标识。</summary>
    public void UpdateLayer(Guid id, Func<ProjectLayer, ProjectLayer> edit)
    {
        ArgumentNullException.ThrowIfNull(edit);
        Apply("Edit layer", document =>
        {
            var found = false;
            var layers = MapLayers(document.Layers, layer =>
            {
                if (layer.Id != id)
                {
                    return layer;
                }

                found = true;
                var next = edit(layer);
                if (next is null || next.Id != id)
                {
                    throw new InvalidOperationException("图层不能为空或改变标识。");
                }

                if (next.Start != layer.Start || next.End != layer.End || next.AnimationOffset != layer.AnimationOffset)
                {
                    next = LayerAnimationTiming.Clip(next);
                }

                return next == layer ? layer : next;
            });
            if (!found)
            {
                throw new KeyNotFoundException("图层不存在。");
            }

            return layers == document.Layers ? document : document with { Layers = layers };
        });
    }

    /// <summary>插入或替换同一属性、同一精确时间的关键帧。</summary>
    public void SetKeyframe(Guid layerId, AnimationProperty property, Keyframe keyframe)
    {
        ArgumentNullException.ThrowIfNull(keyframe);
        UpdateLayer(layerId, layer =>
        {
            if (LayerAnimationTiming.ClampTime(layer, keyframe.Time) != keyframe.Time)
            {
                throw new ArgumentOutOfRangeException(nameof(keyframe), "关键帧必须位于图层片段内。");
            }

            var existing = layer.Tracks.FirstOrDefault(track => track.Property == property);
            if (existing?.Keyframes.FirstOrDefault(frame => frame.Time == keyframe.Time) == keyframe)
            {
                return layer;
            }

            var frames = (existing?.Keyframes ?? []).Where(frame => frame.Time != keyframe.Time)
                .Append(keyframe).OrderBy(frame => frame.Time).ToImmutableArray();
            return layer with
            {
                Tracks = layer.Tracks.Where(track => track.Property != property).Append(new(property, frames)).ToImmutableArray()
            };
        });
    }

    /// <summary>将预设应用到指定层并保留内容与标识。</summary>
    public void ApplyPreset(Guid layerId, EffectPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        UpdateLayer(layerId, layer =>
        {
            var origin = LayerAnimationTiming.GetRange(layer).Minimum;
            var style = layer.SubtitleId is { } subtitleId ? snapshot.Subtitles[FindSubtitle(snapshot, subtitleId)].Style : null;
            var tracks = LegacyAnimationTrackMigration.Merge(preset.Tracks, AnimationProperty.FILL, style?.Fill ?? layer.Fill);
            tracks = LegacyAnimationTrackMigration.Merge(tracks, AnimationProperty.STROKE, style?.Stroke ?? layer.Stroke);
            return LayerAnimationTiming.Clip(layer with
            {
                Tracks = tracks.Select(track => track with
                {
                    Keyframes = track.Keyframes.Select(frame => frame with { Time = frame.Time + origin }).ToImmutableArray()
                }).ToImmutableArray(), MotionPath = preset.MotionPath, Mask = preset.Mask, Blend = preset.Blend
            });
        });
    }

    private void EnsureNotEditing()
    {
        if (editing)
        {
            throw new InvalidOperationException("编辑委托不能重入编辑会话。");
        }
    }

    private static ProjectLayer CreateSubtitleLayer(SubtitleLine line)
    {
        return new() { Id = line.Id, Name = "Subtitle", Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End };
    }

    private static int FindSubtitle(ProjectDocument document, Guid id)
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

    private static ProjectLayer FindLayer(ImmutableArray<ProjectLayer> layers, Guid id)
    {
        foreach (var layer in layers)
        {
            if (layer.Id == id)
            {
                return layer;
            }

            if (layer.Children.Any(child => ContainsLayer(child, id)))
            {
                return FindLayer(layer.Children, id);
            }
        }

        throw new KeyNotFoundException("图层不存在。");
    }

    private static bool ContainsLayer(ProjectLayer layer, Guid id) => layer.Id == id || layer.Children.Any(child => ContainsLayer(child, id));

    private static ImmutableArray<ProjectLayer> MapLayers(ImmutableArray<ProjectLayer> layers, Func<ProjectLayer, ProjectLayer> map)
    {
        ImmutableArray<ProjectLayer>.Builder? changed = null;
        for (var index = 0; index < layers.Length; index++)
        {
            var layer = layers[index];
            var children = MapLayers(layer.Children, map);
            var next = map(children == layer.Children ? layer : layer with { Children = children });
            if (next != layer)
            {
                changed ??= layers.ToBuilder();
                changed[index] = next;
            }
        }

        return changed?.ToImmutable() ?? layers;
    }

    private static ImmutableArray<ProjectLayer> RemoveLayers(ImmutableArray<ProjectLayer> layers, Func<ProjectLayer, bool> remove)
    {
        return layers.Where(layer => !remove(layer)).Select(layer => layer with { Children = RemoveLayers(layer.Children, remove) }).ToImmutableArray();
    }

    private static MediaTime Scale(MediaTime time, MediaTime newDuration, MediaTime oldDuration)
    {
        var numerator = (BigInteger)time.Numerator * newDuration.Numerator * oldDuration.Denominator;
        var denominator = (BigInteger)time.Denominator * newDuration.Denominator * oldDuration.Numerator;
        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new(checked((long)(numerator / divisor)), checked((long)(denominator / divisor)));
    }
}
