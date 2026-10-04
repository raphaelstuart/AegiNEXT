using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Localization;

namespace AegiNext.Desktop.Workspace;

internal sealed class LayerEditingCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
    internal AnimationProperty ActiveProperty => (AnimationProperty)Math.Max(0, session.ViewModel.Effects.Property);
    internal Keyframe? SelectedKeyframe => session.SelectedKeyTime is { } time
        ? session.SelectedLayer?.Tracks.FirstOrDefault(track => track.Property == ActiveProperty)?.Keyframes.FirstOrDefault(frame => frame.Time == time)
        : null;

    internal void UpdateStyle(Func<SubtitleStyle, SubtitleStyle> change)
    {
        if (session.SelectedLayer?.SubtitleId is { } id)
        {
            session.Editor.UpdateSubtitle(id, cue => cue with { Style = change(cue.Style) });
        }
    }

    internal void UpdateColorStyle(Func<SubtitleStyle, SubtitleStyle> style, Func<ProjectLayer, ProjectLayer> layer)
    {
        if (session.SelectedLayer?.Kind == LayerKind.SUBTITLE)
        {
            UpdateStyle(style);
        }
        else
        {
            UpdateLayer(layer);
        }
    }

    internal void UpdateLayer(Func<ProjectLayer, ProjectLayer> change)
    {
        if (session.SelectedLayer is { } layer)
        {
            session.Editor.UpdateLayer(layer.Id, change);
        }
    }

    internal void SetLayerTiming(string text, bool start)
    {
        if (session.SelectedLayer is not { } layer)
        {
            return;
        }

        if (text == TimelineTimeText.Format(start ? layer.Start : layer.End))
        {
            return;
        }

        var time = TimelineTimeText.Parse(text);

        if (layer.SubtitleId is { } id)
        {
            session.Editor.SetSubtitleTiming(id, start ? time : layer.Start, start ? layer.End : time,
                AegiNext.Core.Editing.TimelineEditMode.CROP);
        }
        else
        {
            session.Editor.SetLayerTiming(layer.Id, start ? time : layer.Start, start ? layer.End : time, TimelineEditMode.CROP);
        }
    }

    internal void AddShape(ShapeKind kind)
    {
        var start = session.SelectedCue?.Start ?? session.ProjectPosition;
        var layer = new ProjectLayer
        {
            Name = WorkbenchText.Get(kind == ShapeKind.RECTANGLE ? "Rectangle" : "Ellipse"), Kind = LayerKind.SHAPE,
            Start = start, End = session.SelectedCue?.End ?? start + new MediaTime(5), Shape = new(kind, 300, 180),
            Transform = new(session.Editor.Snapshot.Width / 3d, session.Editor.Snapshot.Height / 3d), Fill = new(0.1, 0.3, 0.9, 0.8)
        };
        session.SelectedLayerId = layer.Id;
        session.Editor.AddLayer(layer);
    }

    internal async Task ImportFontAsync()
    {
        if (session.SelectedLayer?.SubtitleId is not { } id || session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }

        var path = await dialogs.OpenFileAsync("ImportFont", "Fonts", ["*.ttf", "*.otf", "*.ttc"]);
        if (path is null)
        {
            return;
        }

        session.SetProjectBusy(true);
        try
        {
            var asset = await ProjectResources.ImportAsync(path, ProjectAssetKind.FONT, session.ProjectDirectory);
            session.Editor.Apply("Import font", document => document with
            {
                Assets = document.Assets.Add(asset), Subtitles = document.Subtitles.Select(line => line.Id == id
                    ? line with
                    {
                        Style = line.Style with { FontAssetId = asset.Id }
                    }
                    : line).ToImmutableArray()
            });
        }
        finally
        {
            session.SetProjectBusy(false);
        }
    }

    internal async Task ImportImageAsync()
    {
        if (session.IsProjectBusy || !session.TryCommitDrafts())
        {
            return;
        }

        var path = await dialogs.OpenFileAsync("Image", "Images", ["*.png", "*.jpg", "*.jpeg", "*.webp"]);
        if (path is null)
        {
            return;
        }

        session.SetProjectBusy(true);
        try
        {
            var asset = await ProjectResources.ImportAsync(path, ProjectAssetKind.IMAGE, session.ProjectDirectory);
            var start = session.SelectedCue?.Start ?? session.ProjectPosition;
            var layer = new ProjectLayer
            {
                Name = WorkbenchText.Get("Image"), Kind = LayerKind.IMAGE, Start = start,
                End = session.SelectedCue?.End ?? start + new MediaTime(5), Image = new(asset.Id, 300, 180),
                Transform = new(session.Editor.Snapshot.Width / 3d, session.Editor.Snapshot.Height / 3d)
            };
            session.SelectedLayerId = layer.Id;
            session.Editor.Apply("Import image",
                document => document with { Assets = document.Assets.Add(asset), Layers = document.Layers.Add(layer) });
        }
        finally
        {
            session.SetProjectBusy(false);
        }
    }

    internal void GroupLayers()
    {
        var ids = session.ViewModel.Effects.SelectedIds.ToImmutableArray();
        session.Editor.Apply("Group layers",
            document => ProjectEditingOperations.GroupLayers(document, ids, WorkbenchText.Get("Group")));
    }

    internal void MoveLayer(int offset)
    {
        if (session.SelectedLayer is not { } layer)
        {
            return;
        }

        var siblings = FindSiblings(session.Editor.Snapshot.Layers, layer.Id);
        var index = siblings.IndexOf(layer);
        var destination = Math.Clamp(index + offset, 0, siblings.Length - 1);
        if (destination != index)
        {
            session.Editor.Apply("Reorder layer",
                document => ProjectEditingOperations.MoveLayer(document, layer.Id, destination));
        }
    }

    internal static ImmutableArray<ProjectLayer> FindSiblings(ImmutableArray<ProjectLayer> layers, Guid id)
    {
        if (layers.Any(value => value.Id == id))
        {
            return layers;
        }

        foreach (var layer in layers)
        {
            var children = FindSiblings(layer.Children, id);
            if (!children.IsEmpty)
            {
                return children;
            }
        }

        return [];
    }

    internal void BeginPathEdit(bool mask)
    {
        var layer = session.SelectedLayer ?? throw new InvalidOperationException(WorkbenchText.Get("NoSelection"));
        var width = layer.Shape?.Width ?? layer.Image?.Width ?? session.Editor.Snapshot.Width;
        var height = layer.Shape?.Height ?? layer.Image?.Height ?? session.Editor.Snapshot.Height;
        if (mask && layer.Mask is null)
        {
            var path = new PathGeometry(new(0, 0),
            [
                new(new(width / 3, 0), new(width * 2 / 3, 0), new(width, 0)),
                new(new(width, height / 3), new(width, height * 2 / 3), new(width, height)),
                new(new(width * 2 / 3, height), new(width / 3, height), new(0, height)),
                new(new(0, height * 2 / 3), new(0, height / 3), new(0, 0))
            ], true);
            session.Editor.UpdateLayer(layer.Id, value => value with { Mask = new(path) });
        }
        else if (!mask && layer.MotionPath is null)
        {
            var path = new PathGeometry(new(0, 0), [new(new(120, -120), new(240, 120), new(360, 0))]);
            session.Editor.UpdateLayer(layer.Id, value => value with { MotionPath = new(path, layer.End - layer.Start) });
        }

        session.ViewModel.Effects.EditMode = mask ? CanvasEditMode.MASK : CanvasEditMode.PATH;

    }

    internal void CreateKaraoke()
    {
        var cue = session.SelectedCue ?? throw new InvalidOperationException(WorkbenchText.Get("NoSelection"));
        var boundaries = StringInfo.ParseCombiningCharacters(cue.Text);
        if (boundaries.Length == 0)
        {
            return;
        }

        var duration = cue.End - cue.Start;
        var offset = session.SelectedLayer?.AnimationOffset ?? MediaTime.Zero;
        var segments = ImmutableArray.CreateBuilder<KaraokeSegment>();
        for (var index = 0; index < boundaries.Length; index++)
        {
            var start = new MediaTime(checked(duration.Numerator * index),
                checked(duration.Denominator * boundaries.Length)) + offset;
            var end = new MediaTime(checked(duration.Numerator * (index + 1)),
                checked(duration.Denominator * boundaries.Length)) + offset;
            if (end <= MediaTime.Zero)
            {
                continue;
            }

            segments.Add(new(boundaries[index],
                (index + 1 < boundaries.Length ? boundaries[index + 1] : cue.Text.Length) - boundaries[index],
                start < MediaTime.Zero ? MediaTime.Zero : start, end, new(1, 0.6, 0)));
        }

        session.Editor.UpdateSubtitle(cue.Id, value => value with { Karaoke = segments.ToImmutable() });
    }

    internal void ClearKeyframeSelection()
    {
        session.SelectedKeyTime = null;
        session.ViewModel.Effects.CanDeleteKeyframe = false;
        session.CancelCanvasGesture();
    }

    internal void SelectKeyframe(TimelineKeyframeEventArgs e)
    {
        if (!session.TryCommitDrafts())
        {
            return;
        }
        var layer = session.SelectedLayer;
        if (layer is null || layer.Id != e.LayerId || layer.Tracks
                .FirstOrDefault(track => track.Property == e.Property)?.Keyframes
                .FirstOrDefault(frame => frame.Time == e.OldTime) is null)
        {
            return;
        }

        session.ViewModel.CancelGestures();
        var wasUpdating = session.IsUpdating;
        session.IsUpdating = true;
        try
        {
            session.SelectedKeyTime = e.OldTime;
            session.ViewModel.Effects.Property = (int)e.Property;
            session.ViewModel.Timeline.EffectProperty = e.Property;
            session.RefreshDocument();
            RefreshKeyframeInspector();
            _ = session.RunCommandAsync(async () =>
            {
                await session.PauseForSceneEditAsync();
                if (!session.IsClosing && session.SelectedLayerId == e.LayerId && session.SelectedKeyTime == e.OldTime)
                {
                    await session.SeekForEditingAsync(layer.Start + e.OldTime - layer.AnimationOffset);
                }
            });
        }
        finally
        {
            session.IsUpdating = wasUpdating;
        }
    }

    internal void RefreshKeyframeInspector()
    {
        var wasUpdating = session.IsUpdating;
        session.IsUpdating = true;
        try
        {
            var frame = SelectedKeyframe;

            var (minimum, maximum) = ActiveProperty switch
            {
                AnimationProperty.OPACITY or AnimationProperty.FILL_ALPHA or AnimationProperty.STROKE_ALPHA
                    or AnimationProperty.PATH_PROGRESS => (0m, 1m),
                AnimationProperty.BLUR => (0m, 512m),
                AnimationProperty.STROKE_WIDTH => (0m, 4096m),
                AnimationProperty.SCALE_X or AnimationProperty.SCALE_Y => (-10000m, 10000m),
                AnimationProperty.FILL_RED or AnimationProperty.FILL_GREEN or AnimationProperty.FILL_BLUE
                    or AnimationProperty.STROKE_RED or AnimationProperty.STROKE_GREEN
                    or AnimationProperty.STROKE_BLUE => (-65504m, 65504m),
                _ => (-1000000000m, 1000000000m)
            };
            var input = session.ViewModel.Effects;
            input.KeyframeMaximum = maximum;
            input.KeyframeMinimum = minimum;
            input.KeyframeValue = frame is not null
                ? (decimal)frame.Value
                : Math.Clamp(input.KeyframeValue ?? 0, minimum, maximum);
            if (frame is not null)
            {
                session.ViewModel.Effects.Interpolation = (int)frame.Interpolation;
            }

            RefreshKeyframeAvailability();
            session.ViewModel.Effects.CanDeleteKeyframe = frame is not null;
        }
        finally
        {
            session.IsUpdating = wasUpdating;
        }
    }

    internal void UpdateSelectedKeyframe(Func<Keyframe, Keyframe> change)
    {
        if (session.SelectedLayer is { } layer && SelectedKeyframe is { } frame)
        {
            session.Editor.SetKeyframe(layer.Id, ActiveProperty, change(frame));
        }
    }

    internal void AddKeyframe()
    {
        var layer = session.SelectedLayer ?? throw new InvalidOperationException(WorkbenchText.Get("NoSelection"));
        var time = session.ProjectPosition - layer.Start + layer.AnimationOffset;
        if (session.ProjectPosition < layer.Start || session.ProjectPosition > layer.End ||
            time < MediaTime.Zero)
        {
            return;
        }

        session.Editor.SetKeyframe(layer.Id, ActiveProperty, new(time,
            (double)(session.ViewModel.Effects.KeyframeValue ?? 0),
            (KeyframeInterpolation)Math.Max(0, session.ViewModel.Effects.Interpolation)));
        session.ViewModel.Timeline.EffectProperty = ActiveProperty;
        SelectKeyframe(new(layer.Id, ActiveProperty, time, time));
    }

    internal void DeleteKeyframe()
    {
        var layer = session.SelectedLayer ?? throw new InvalidOperationException(WorkbenchText.Get("NoSelection"));
        var time = session.SelectedKeyTime ?? session.ProjectPosition - layer.Start + layer.AnimationOffset;
        session.Editor.UpdateLayer(layer.Id, value => value with
        {
            Tracks = value.Tracks.Select(track => track.Property == ActiveProperty
                    ? track with
                    {
                        Keyframes = track.Keyframes.Where(frame => frame.Time != time).ToImmutableArray()
                    }
                    : track)
                .Where(track => !track.Keyframes.IsEmpty).ToImmutableArray()
        });
        ClearKeyframeSelection();
    }

    internal void MoveKeyframe(TimelineKeyframeEventArgs e)
    {
        var movedTime = e.NewTime;
        session.Editor.UpdateLayer(e.LayerId, layer =>
        {
            movedTime = LayerAnimationTiming.ClampTime(layer, e.NewTime);
            return layer with
            {
                Tracks = layer.Tracks.Select(track => track.Property == e.Property
                ? track with
                {
                    Keyframes = track.Keyframes
                        .Where(frame => frame.Time != movedTime || frame.Time == e.OldTime)
                        .Select(frame =>
                            frame.Time == e.OldTime
                                ? frame with { Time = movedTime, Value = e.NewValue ?? frame.Value }
                                : frame).OrderBy(frame => frame.Time).ToImmutableArray()
                }
                : track).ToImmutableArray()
            };
        });
        SelectKeyframe(new(e.LayerId, e.Property, movedTime, movedTime));
    }

    internal void RefreshKeyframeAvailability()
    {
        session.ViewModel.Effects.CanAddKeyframe = session.SelectedLayer is { } layer &&
            session.ProjectPosition >= layer.Start && session.ProjectPosition <= layer.End &&
            session.ProjectPosition - layer.Start + layer.AnimationOffset >= MediaTime.Zero;
    }

    internal void SavePreset()
    {
        var layer = session.SelectedLayer ?? throw new InvalidOperationException(WorkbenchText.Get("NoSelection"));
        var name = session.ViewModel.Effects.PresetName;
        var origin = LayerAnimationTiming.GetRange(layer).Minimum;
        var preset = new EffectPreset(Guid.NewGuid(),
            string.IsNullOrWhiteSpace(name)
                ? WorkbenchText.Get("Preset") + " " + (session.Editor.Snapshot.Presets.Length + 1)
                : name,
            layer.Tracks.Select(track => track with
            {
                Keyframes = track.Keyframes.Select(frame => frame with
                {
                    Time = frame.Time - origin
                }).ToImmutableArray()
            }).ToImmutableArray(), layer.MotionPath, layer.Mask, layer.Blend);
        session.Editor.Apply("Save preset", document => document with { Presets = document.Presets.Add(preset) });
    }

    internal void ApplyBuiltinPreset(string name)
    {
        var layer = session.SelectedLayer ?? throw new InvalidOperationException(WorkbenchText.Get("NoSelection"));
        var (offset, maximum) = LayerAnimationTiming.GetRange(layer);
        var duration = maximum - offset;
        if (duration <= MediaTime.Zero)
        {
            throw new InvalidOperationException("片段中没有可应用动画的时间。");
        }

        var edge = new MediaTime(1, 4);
        if (duration < edge + edge)
        {
            edge = new(duration.Numerator, checked(duration.Denominator * 3));
        }

        var tracks = name switch
        {
            "Fade" => ImmutableArray.Create(new AnimationTrack(AnimationProperty.OPACITY,
                [new(offset, 0), new(offset + edge, 1), new(offset + duration - edge, 1), new(offset + duration, 0)])),
            "Pop" => ImmutableArray.Create(new AnimationTrack(AnimationProperty.SCALE_X,
                    [new(offset, 0.2, KeyframeInterpolation.EASE_OUT), new(offset + edge, 1)]),
                new AnimationTrack(AnimationProperty.SCALE_Y,
                    [new(offset, 0.2, KeyframeInterpolation.EASE_OUT), new(offset + edge, 1)])),
            _ => ImmutableArray.Create(new AnimationTrack(AnimationProperty.POSITION_X,
            [
                new(offset, layer.Transform.X - 250, KeyframeInterpolation.EASE_OUT),
                new(offset + edge, layer.Transform.X)
            ]))
        };
        session.Editor.UpdateLayer(layer.Id, value => value with
        {
            Tracks = value.Tracks.Where(track => !tracks.Any(added => added.Property == track.Property)).Concat(tracks)
                .ToImmutableArray()
        });
        session.ViewModel.Effects.Property = (int)tracks[0].Property;
    }

    internal void AddRectangle() => AddShape(ShapeKind.RECTANGLE);
    internal void AddEllipse() => AddShape(ShapeKind.ELLIPSE);
    internal void DeleteLayer()
    {
        if (session.SelectedLayer is { } layer)
        {
            session.Editor.Apply("Remove layer", document => ProjectEditingOperations.RemoveLayer(document, layer.Id));
        }
    }
    internal void UngroupLayer()
    {
        if (session.SelectedLayer is { } layer)
        {
            session.Editor.Apply("Ungroup layer", document => ProjectEditingOperations.UngroupLayer(document, layer.Id));
        }
    }
    internal void MoveLayerUp() => MoveLayer(1);
    internal void MoveLayerDown() => MoveLayer(-1);
    internal void EditPath() => BeginPathEdit(false);
    internal void EditMask() => BeginPathEdit(true);
    internal void ClearPath() => UpdateLayer(layer => layer with { MotionPath = null });
    internal void ClearMask() => UpdateLayer(layer => layer with { Mask = null });
    internal void ApplyFade() => ApplyBuiltinPreset("Fade");
    internal void ApplyPop() => ApplyBuiltinPreset("Pop");
    internal void ApplySlide() => ApplyBuiltinPreset("Slide");
    internal void ApplySelectedPreset()
    {
        if (session.SelectedLayer is { } layer && session.ViewModel.Effects.Preset is var index && index >= 0)
        {
            session.Editor.ApplyPreset(layer.Id, session.Editor.Snapshot.Presets[index]);
        }
    }
    internal void ClearKaraoke()
    {
        if (session.SelectedCue is { } cue)
        {
            session.Editor.UpdateSubtitle(cue.Id, line => line with { Karaoke = [] });
        }
    }
}
