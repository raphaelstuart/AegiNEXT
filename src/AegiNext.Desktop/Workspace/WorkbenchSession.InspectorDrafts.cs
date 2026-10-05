using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private ProjectDocument PrepareInspectorDrafts(ProjectDocument document, bool preview)
    {
        var prepared = document;
        var selected = SelectedLayer;
        if (stylesDirty && selected is not null)
        {
            var vm = ViewModel.Styles;
            ViewModel.InvalidPanelId = "styles";
            ViewModel.InvalidFieldKey = "FontCombo";
            var family = (preview ? vm.FontFamily : vm.FontDraft)?.Trim() ?? string.Empty;
            if (family.Length is 0 or > 512 || family.Any(char.IsControl))
            {
                throw new InvalidDataException(Localization.Get("Workbench.Font"));
            }
            if (selected.SubtitleId is { } id)
            {
                var line = prepared.Subtitles.Single(value => value.Id == id);
                var fontSize = ReadNumber(vm.FontSizeText, line.Style.FontSize, "Size", "FontSizeInput");
                var strokeWidth = PrepareStrokeWidth(ref prepared, selected, line.Style.StrokeWidth, vm.StrokeWidthText);
                var fill = PrepareColor(ref prepared, selected, ReadColorDraft(vm.FillDraft, "FillPicker"), line.Style.Fill, false);
                var stroke = PrepareColor(ref prepared, selected, ReadColorDraft(vm.StrokeDraft, "StrokePicker"), line.Style.Stroke, true);
                if (fontSize <= 0 || strokeWidth < 0 || string.IsNullOrWhiteSpace(vm.FontFamily))
                {
                    throw new InvalidDataException(Localization.Get("Workbench.Font") + ": " + Localization.Get("Workbench.Size"));
                }

                var familyChanged = family != line.Style.FontFamily;
                if (vm.Position.Validate() is { } positionKey)
                {
                    ViewModel.InvalidFieldKey = positionKey;
                    throw new InvalidDataException(Localization.Get("Workbench.ExplicitPosition"));
                }
                var style = line.Style with
                {
                    FontFamily = family,
                    FontAssetId = familyChanged ? null : line.Style.FontAssetId,
                    FontSize = fontSize,
                    StrokeWidth = strokeWidth,
                    Fill = fill,
                    Stroke = stroke,
                    Bold = vm.Bold == true,
                    Italic = vm.Italic == true,
                    Alignment = alignments[Math.Clamp(vm.Alignment, 0, alignments.Length - 1)],
                    Position = vm.Position.CreatePosition()
                };
                prepared = WorkspaceDraftOperations.UpdateSubtitle(prepared, id, cue => cue with { Style = style });
            }
            else
            {
                var fill = PrepareColor(ref prepared, selected, ReadColorDraft(vm.FillDraft, "FillPicker"), selected.Fill, false);
                var stroke = PrepareColor(ref prepared, selected, ReadColorDraft(vm.StrokeDraft, "StrokePicker"), selected.Stroke, true);
                var width = PrepareStrokeWidth(ref prepared, selected, selected.StrokeWidth, vm.StrokeWidthText);
                prepared = WorkspaceDraftOperations.UpdateLayer(prepared, selected.Id, layer => layer with
                {
                    Fill = fill, Stroke = stroke, StrokeWidth = width
                });
            }
        }

        if (effectsDirty && selected is not null)
        {
            var vm = ViewModel.Effects;
            ViewModel.InvalidPanelId = "effects";
            var keyframeChanged = changedEffectFields.Overlaps(["KeyframeValueText", "KeyframeValue", "KeyframeValueY", "KeyframeValueYText", "KeyframeColorDraft"]);
            var keyframeValue = ReadKeyframeDraft(vm, keyframeChanged);
            ViewModel.InvalidFieldKey = "LayerStartInput";
            var start = vm.LayerStart == TimelineTimeText.Format(selected.Start) ? selected.Start : TimelineTimeText.Parse(vm.LayerStart);
            ViewModel.InvalidFieldKey = "LayerEndInput";
            var end = vm.LayerEnd == TimelineTimeText.Format(selected.End) ? selected.End : TimelineTimeText.Parse(vm.LayerEnd);
            if (selected.SubtitleId is { } id && (start != selected.Start || end != selected.End))
            {
                prepared = WorkspaceDraftOperations.UpdateSubtitle(prepared, id, line => line with { Start = start, End = end });
            }

            var originalPlacement = ResolvePlacement(editor.Snapshot, selected);
            var preparedLayer = Flatten(prepared.Layers).Single(value => value.Id == selected.Id);
            var preparedPlacement = ResolvePlacement(prepared, preparedLayer);
            var target = AnimationTarget!;
            var positionX = InspectorVector(selected, AnimationProperty.POSITION, selected.Transform.Position).X;
            var positionY = InspectorVector(selected, AnimationProperty.POSITION, selected.Transform.Position).Y;
            if (originalPlacement.BasePosition is { } originalBase)
            {
                var displayedX = originalBase.X + positionX;
                var displayedY = originalBase.Y + positionY;
                var requestedX = ReadEffectNumber(vm.PositionXText, displayedX, "PositionX", "PositionXInput", "PositionXText");
                var requestedY = ReadEffectNumber(vm.PositionYText, displayedY, "PositionY", "PositionYInput", "PositionYText");
                if (requestedX != displayedX || requestedY != displayedY)
                {
                    if (preparedPlacement.BasePosition is not { } preparedBase)
                    {
                        throw new InvalidDataException(Localization.Get("Workbench.SubtitlePositionUnavailable"), preparedPlacement.Error);
                    }
                    positionX = requestedX == displayedX ? positionX : requestedX - preparedBase.X;
                    positionY = requestedY == displayedY ? positionY : requestedY - preparedBase.Y;
                }
            }
            else if (!string.IsNullOrEmpty(vm.PositionXText) || !string.IsNullOrEmpty(vm.PositionYText))
            {
                throw new InvalidDataException(Localization.Get("Workbench.SubtitlePositionUnavailable"), originalPlacement.Error);
            }

            prepared = WorkspaceDraftOperations.UpdateLayer(prepared, selected.Id, layer => layer with
            {
                Start = start,
                End = end,
                AnimationOffset = layer.AnimationOffset + start - layer.Start,
                Shape = layer.Shape is { } shape ? shape with
                {
                    Width = ReadNumber(vm.LayerWidthText, shape.Width, "Size", "LayerWidthInput"), Height = ReadNumber(vm.LayerHeightText, shape.Height, "Size", "LayerHeightInput")
                } : null,
                Image = layer.Image is { } image ? image with
                {
                    Width = ReadNumber(vm.LayerWidthText, image.Width, "Size", "LayerWidthInput"), Height = ReadNumber(vm.LayerHeightText, image.Height, "Size", "LayerHeightInput")
                } : null,
                Blend = (BlendMode)vm.Blend,
                MotionPath = layer.MotionPath is { } path ? path with { OrientToPath = vm.OrientPath == true } : null,
                Mask = layer.Mask is { } mask ? mask with { Inverted = vm.InvertMask == true } : null
            });
            foreach (var (property, text, fallback, label, field) in new[]
            {
                (AnimationProperty.ROTATION, vm.RotationText, selected.Transform.Rotation, "Rotation", "RotationInput"),
                (AnimationProperty.OPACITY, vm.OpacityText, selected.Opacity, "Opacity", "OpacityInput"),
                (AnimationProperty.BLUR, vm.BlurText, selected.Blur, "Blur", "BlurInput")
            })
            {
                var original = InspectorValue(selected, property, fallback);
                var requested = ReadEffectNumber(text, original, label, field, label + "Text");
                if (requested != original)
                {
                    prepared = AnimationEditOperations.SetValue(prepared, target, property, requested);
                }
            }
            var originalPosition = InspectorVector(selected, AnimationProperty.POSITION, selected.Transform.Position);
            var requestedPosition = new ScenePoint(positionX, positionY);
            if (requestedPosition != originalPosition)
            {
                prepared = AnimationEditOperations.SetValue(prepared, target, AnimationProperty.POSITION, requestedPosition);
            }
            var originalScale = InspectorVector(selected, AnimationProperty.SCALE, selected.Transform.Scale);
            var requestedScale = new ScenePoint(
                ReadEffectNumber(vm.ScaleXText, originalScale.X, "ScaleX", "ScaleXInput", "ScaleXText"),
                ReadEffectNumber(vm.ScaleYText, originalScale.Y, "ScaleY", "ScaleYInput", "ScaleYText"));
            if (requestedScale != originalScale)
            {
                prepared = AnimationEditOperations.SetValue(prepared, target, AnimationProperty.SCALE, requestedScale);
            }
            if (keyframeChanged)
            {
                prepared = AnimationEditOperations.SetValue(prepared, target, target.Property ?? ActiveProperty, keyframeValue);
            }
            if (target.IsKeyframe && (keyframeChanged || changedEffectFields.Contains("Interpolation")))
            {
                var property = target.Property ?? ActiveProperty;
                var frame = selected.Tracks.FirstOrDefault(track => track.Property == property)?.Keyframes.FirstOrDefault(key => key.Time == target.LocalTime) ?? new Keyframe(target.LocalTime, keyframeValue);
                prepared = WorkspaceDraftOperations.SetKeyframe(prepared, selected.Id, property, frame with
                {
                    Value = keyframeChanged ? keyframeValue : frame.Value,
                    Interpolation = (KeyframeInterpolation)vm.Interpolation,
                    CurveStart = vm.Interpolation == (int)frame.Interpolation ? frame.CurveStart : 0,
                    CurveEnd = vm.Interpolation == (int)frame.Interpolation ? frame.CurveEnd : 1,
                    ComponentCurves = vm.Interpolation == (int)frame.Interpolation ? frame.ComponentCurves : []
                });
            }
        }

        return prepared;
    }
}
