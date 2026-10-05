using System.Collections.Immutable;
using System.Globalization;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Localization;

namespace AegiNext.Desktop.Workspace;

internal sealed class LayerEditingCoordinator(WorkbenchSession session, IWorkbenchDialogService dialogs)
{
    private Guid? valueLayerId;
    private AnimationProperty? valueProperty;

    internal AnimationProperty ActiveProperty => session.ViewModel.Effects.Property;
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

    internal void BeginPathEdit()
    {
        var layer = session.SelectedLayer ?? throw new InvalidOperationException(WorkbenchText.Get("NoSelection"));
        if (layer.MotionPath is null)
        {
            var path = new PathGeometry(new(0, 0), [new(new(120, -120), new(240, 120), new(360, 0))]);
            session.Editor.UpdateLayer(layer.Id, value => value with { MotionPath = new(path, layer.End - layer.Start) });
        }
        session.ViewModel.Effects.EditMode = CanvasEditMode.PATH;
    }

    internal void AddPathPoint()
    {
        UpdateLayer(layer =>
        {
            var path = layer.MotionPath?.Path ?? new PathGeometry(new(0, 0), [new(new(40, 0), new(80, 0), new(120, 0))]);
            var end = path.Segments[^1].End;
            var edited = PathOperations.AppendPoint(path, new(end.X + 120, end.Y));
            return layer with { MotionPath = new(edited, layer.MotionPath?.Duration ?? layer.End - layer.Start, layer.MotionPath?.OrientToPath ?? false) };
        });
        session.ViewModel.Effects.EditMode = CanvasEditMode.PATH;
    }

    internal void RemovePathPoint()
    {
        if (session.SelectedLayer?.MotionPath is not { } path || path.Path.Segments.Length <= 1)
        {
            return;
        }
        UpdateLayer(layer => layer with { MotionPath = path with { Path = PathOperations.RemovePoint(path.Path, path.Path.Segments.Length) } });
    }

    internal void ResetPositionEffects()
    {
        if (session.SelectedLayer?.SubtitleId is { } id)
        {
            session.ViewModel.CancelGestures();
            ClearKeyframeSelection();
            session.Editor.ResetSubtitlePositionEffects(id);
            session.ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
        }
    }

    internal void ResetAutomaticPosition()
    {
        if (session.SelectedCue is { } cue)
        {
            session.ViewModel.CancelGestures();
            ClearKeyframeSelection();
            session.Editor.ResetSubtitlePosition(cue.Id);
            session.ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
        }
    }

    internal void CreateKaraoke(Guid? presetId)
    {
        var cue = session.SelectedCue ?? throw new InvalidOperationException(WorkbenchText.Get("NoSelection"));
        var preset = presetId is { } id
            ? session.StyleLibrary.Snapshot.Presets.FirstOrDefault(value => value.Id == id)
            : null;
        var highlightStyle = preset is not null
            ? KaraokeHighlightStyle.FromStyle(preset.Id, preset.Name, preset.Style)
            : presetId is { } existingId && cue.KaraokeStyle?.PresetId == existingId
                ? cue.KaraokeStyle
                : null;
        if (!cue.Karaoke.IsEmpty)
        {
            session.Editor.UpdateSubtitle(cue.Id, value => value with { KaraokeStyle = highlightStyle });
            return;
        }

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

        session.Editor.UpdateSubtitle(cue.Id, value => value with { Karaoke = segments.ToImmutable(), KaraokeStyle = highlightStyle });
    }

    internal void ClearKeyframeSelection()
    {
        session.SelectedKeyTime = null;
        session.ViewModel.Effects.CanDeleteKeyframe = false;
        session.CancelCanvasGesture();
    }

    internal bool SelectKeyframe(TimelineKeyframeEventArgs e)
    {
        if (!session.TryCommitDrafts())
        {
            return false;
        }
        var layer = WorkbenchSession.Flatten(session.Editor.Snapshot.Layers).FirstOrDefault(value => value.Id == e.LayerId);
        if (layer is null || layer.Tracks
                .FirstOrDefault(track => track.Property == e.Property)?.Keyframes
                .FirstOrDefault(frame => frame.Time == e.OldTime) is null)
        {
            return false;
        }

        session.ViewModel.CancelGestures();
        var wasUpdating = session.IsUpdating;
        session.IsUpdating = true;
        try
        {
            session.SelectedLayerId = layer.Id;
            session.SelectedCueId = layer.SubtitleId;
            session.ViewModel.Effects.SelectedIds = [layer.Id];
            session.SelectedKeyTime = e.OldTime;
            session.ViewModel.Effects.Property = e.Property;
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

        return true;
    }

    internal void RefreshKeyframeInspector()
    {
        var wasUpdating = session.IsUpdating;
        session.IsUpdating = true;
        try
        {
            var frame = SelectedKeyframe;

            var minimum = (decimal)AnimationPropertyMetadata.GetMinimum(ActiveProperty, 0);
            var maximum = (decimal)AnimationPropertyMetadata.GetMaximum(ActiveProperty, 0);
            var input = session.ViewModel.Effects;
            input.KeyframeMaximum = maximum;
            input.KeyframeMinimum = minimum;
            var fallback = session.SelectedLayer is { } layer ? BaseValue(layer, ActiveProperty) : AnimationValue.FromScalar(0);
            var track = session.SelectedLayer?.Tracks.FirstOrDefault(value => value.Property == ActiveProperty);
            var current = frame?.Value ?? (track is not null && session.AnimationTarget is { } target
                ? SceneEvaluator.EvaluateTrack(track, target.LocalTime) : fallback);
            if (frame is not null || track is not null || valueLayerId != session.SelectedLayerId || valueProperty != ActiveProperty)
            {
                if (current.IsColor)
                {
                    input.KeyframeColorDraft.Load(current.Color, !session.HasEffectDrafts);
                }
                else
                {
                    input.KeyframeValue = Math.Clamp((decimal)current.GetComponent(0), minimum, maximum);
                    input.KeyframeValueY = current.IsVector ? Math.Clamp((decimal)current.Vector.Y, minimum, maximum) : 0;
                }
            }
            valueLayerId = session.SelectedLayerId;
            valueProperty = ActiveProperty;
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

    private AnimationValue BaseValue(ProjectLayer layer, AnimationProperty property)
    {
        var style = layer.SubtitleId is { } id
            ? session.DocumentSnapshot.Subtitles.Single(value => value.Id == id).Style : null;
        return property switch
        {
            AnimationProperty.POSITION => layer.Transform.Position,
            AnimationProperty.SCALE => layer.Transform.Scale,
            AnimationProperty.ROTATION => layer.Transform.Rotation,
            AnimationProperty.OPACITY => layer.Opacity,
            AnimationProperty.BLUR => layer.Blur,
            AnimationProperty.STROKE_WIDTH => style?.StrokeWidth ?? layer.StrokeWidth,
            AnimationProperty.FILL => style?.Fill ?? layer.Fill,
            AnimationProperty.STROKE => style?.Stroke ?? layer.Stroke,
            _ => 0
        };
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

        var value = ActiveProperty is AnimationProperty.FILL or AnimationProperty.STROKE
            ? AnimationValue.FromColor(session.ViewModel.Effects.KeyframeColorDraft.Value)
            : ActiveProperty is AnimationProperty.POSITION or AnimationProperty.SCALE
            ? AnimationValue.FromVector(new((double)(session.ViewModel.Effects.KeyframeValue ?? 0), (double)(session.ViewModel.Effects.KeyframeValueY ?? 0)))
            : AnimationValue.FromScalar((double)(session.ViewModel.Effects.KeyframeValue ?? 0));
        session.Editor.SetKeyframe(layer.Id, ActiveProperty, new(time,
            value,
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

    internal void EditPath() => BeginPathEdit();
    internal void ClearPath() => UpdateLayer(layer => layer with { MotionPath = null, Tracks = layer.Tracks.Where(track => track.Property != AnimationProperty.PATH_PROGRESS).ToImmutableArray() });
    internal void ApplySelectedPreset() => session.EffectScripts.ApplySelected();
    internal void ClearKaraoke()
    {
        if (session.SelectedCue is { } cue)
        {
            session.Editor.UpdateSubtitle(cue.Id, line => line with { Karaoke = [], KaraokeStyle = null });
        }
    }
}
