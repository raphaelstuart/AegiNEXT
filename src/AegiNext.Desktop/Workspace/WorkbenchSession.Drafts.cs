using System.Collections.Immutable;
using System.ComponentModel;
using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Localization;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private static readonly HashSet<string> styleDraftProperties =
    [
        "FontFamily", "FontDraft", "FontSize", "FontSizeText", "StrokeWidth", "StrokeWidthText", "Fill", "Stroke", "FillDraft", "StrokeDraft", "Bold", "Italic", "Alignment", "Position"
    ];
    private static readonly HashSet<string> effectDraftProperties =
    [
        "LayerStart", "LayerEnd", "LayerWidth", "LayerWidthText", "LayerHeight", "LayerHeightText", "PositionX", "PositionXText", "PositionY", "PositionYText", "ScaleX", "ScaleXText", "ScaleY", "ScaleYText",
        "Rotation", "RotationText", "Opacity", "OpacityText", "Blur", "BlurText", "Blend", "InvertMask", "OrientPath", "KeyframeValue", "KeyframeValueText", "KeyframeValueY", "KeyframeValueYText", "KeyframeColorDraft", "Interpolation"
    ];

    private readonly HashSet<string> changedEffectFields = [];

    internal bool TryCommitDrafts(bool focusInvalid = true)
    {
        if (updatingWorkbench || closing)
        {
            return !closing;
        }

        var document = editor.Snapshot;
        var prepared = document;
        Guid? invalidRow = null;
        try
        {
            var changes = new Dictionary<Guid, SubtitleLine>();
            foreach (var row in ViewModel.Subtitles.Rows.Where(value => value.IsDirty))
            {
                invalidRow = row.Id;
                ViewModel.InvalidPanelId = "subtitles";
                ViewModel.InvalidFieldKey = row.StartText != TimelineTimeText.Format(row.Original.Start) ? "StartText" : row.EndText != TimelineTimeText.Format(row.Original.End) ? "EndText" : "Text";
                var line = document.Subtitles.FirstOrDefault(value => value.Id == row.Id);
                if (line is not null)
                {
                    changes.Add(row.Id, row.CreateEditedLine(line));
                }
            }

            invalidRow = null;
            foreach (var change in changes.Values)
            {
                prepared = WorkspaceDraftOperations.UpdateSubtitle(prepared, change.Id, _ => change);
            }

            var selected = SelectedLayer;
            if (stylesDirty && selected is not null)
            {
                var vm = ViewModel.Styles;
                ViewModel.InvalidPanelId = "styles";
                ViewModel.InvalidFieldKey = "FontCombo";
                var family = vm.FontDraft?.Trim() ?? string.Empty;
                if (family.Length is 0 or > 512 || family.Any(char.IsControl))
                {
                    throw new InvalidDataException(WorkbenchText.Get("Font"));
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
                        throw new InvalidDataException(WorkbenchText.Get("Font") + ": " + WorkbenchText.Get("Size"));
                    }

                    var familyChanged = family != line.Style.FontFamily;
                    if (vm.Position.Validate() is { } positionKey)
                    {
                        ViewModel.InvalidFieldKey = positionKey;
                        throw new InvalidDataException(WorkbenchText.Get("ExplicitPosition"));
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

                var originalPlacement = ResolvePlacement(document, selected);
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
                            throw new InvalidDataException(WorkbenchText.Get("SubtitlePositionUnavailable"), preparedPlacement.Error);
                        }
                        positionX = requestedX == displayedX ? positionX : requestedX - preparedBase.X;
                        positionY = requestedY == displayedY ? positionY : requestedY - preparedBase.Y;
                    }
                }
                else if (!string.IsNullOrEmpty(vm.PositionXText) || !string.IsNullOrEmpty(vm.PositionYText))
                {
                    throw new InvalidDataException(WorkbenchText.Get("SubtitlePositionUnavailable"), originalPlacement.Error);
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

            ProjectValidator.Validate(prepared);
            ViewModel.InvalidPanelId = "export";
            var crf = ViewModel.Export.Crf ?? 20;
            var videoBitrate = ViewModel.Export.VideoBitrate ?? 8;
            if (ViewModel.Export.UseHardwareEncoder)
            {
                videoBitrate = (decimal)RequiredNumber(ViewModel.Export.VideoBitrateText, "VideoBitrate", "VideoBitrateInput");
            }
            else
            {
                crf = (decimal)RequiredNumber(ViewModel.Export.CrfText, "Quality", "CrfInput");
            }
            if (!ViewModel.Export.UseHardwareEncoder && (crf is < 0 or > 51 || crf != Math.Truncate(crf)))
            {
                throw new InvalidDataException(WorkbenchText.Get("Quality"));
            }
            var bitrate = RequiredNumber(ViewModel.Export.AudioBitrateText, "AudioBitrate", "AudioBitrateInput");
            if (bitrate is < 32 or > 512 || bitrate != Math.Truncate(bitrate))
            {
                throw new InvalidDataException(WorkbenchText.Get("AudioBitrate"));
            }
            if (ViewModel.Export.Codec is < 0 or > 2 || ViewModel.Export.Speed is < 0 or > 2 || ViewModel.Export.AudioMode is < 0 or > 2)
            {
                ViewModel.InvalidFieldKey = "CodecCombo";
                throw new InvalidDataException(WorkbenchText.Get("Codec"));
            }
            ViewModel.Export.Crf = (decimal)crf;
            ViewModel.Export.VideoBitrate = videoBitrate;
            ViewModel.Export.AudioBitrate = (decimal)bitrate;
            var originalStylesDirty = stylesDirty;
            var originalEffectsDirty = effectsDirty;
            stylesDirty = false;
            effectsDirty = false;
            var originalFields = changedEffectFields.ToArray();
            var originalTarget = SceneEditing.DraftTarget;
            SceneEditing.DraftTarget = null;
            changedEffectFields.Clear();
            try
            {
                if (prepared != document)
                {
                    InvalidateTimingSession();
                    editor.Apply("Commit workspace drafts", _ => prepared);
                }
                else
                {
                    RefreshDocument();
                }
            }
            catch
            {
                stylesDirty = originalStylesDirty;
                effectsDirty = originalEffectsDirty;
                changedEffectFields.UnionWith(originalFields);
                SceneEditing.DraftTarget = originalTarget;
                throw;
            }

            ViewModel.InvalidPanelId = null;
            ViewModel.InvalidFieldKey = null;
            ViewModel.Subtitles.ValidationError = null;
            ViewModel.Subtitles.InvalidRowId = null;
            ViewModel.Effects.ValidationError = null;
            ViewModel.Effects.InvalidFieldKey = null;
            lastDraftDiagnostic = null;
            return true;
        }
        catch (Exception error)
        {
            ViewModel.Subtitles.InvalidRowId = invalidRow;
            ViewModel.Subtitles.ValidationError = error.Message;
            ReportDraftError(error, focusInvalid);
            return false;
        }
    }

    private AnimationValue ReadKeyframeDraft(Panels.Effects.EffectsPanelViewModel vm, bool changed)
    {
        var property = SceneEditing.DraftTarget?.Property ?? ActiveProperty;
        var vector = property is AnimationProperty.POSITION or AnimationProperty.SCALE;
        if (property is AnimationProperty.FILL or AnimationProperty.STROKE)
        {
            return ReadColorDraft(vm.KeyframeColorDraft, "KeyframeColorInput");
        }
        var previous = SelectedLayer?.Tracks.FirstOrDefault(track => track.Property == property)?.Keyframes
            .FirstOrDefault(frame => frame.Time == AnimationTarget?.LocalTime)?.Value;
        double Read(string text, string field)
        {
            var value = RequiredNumber(text, "Property", field);
            if (value < (double)vm.KeyframeMinimum || value > (double)vm.KeyframeMaximum)
            {
                throw new InvalidDataException(WorkbenchText.Get("Property"));
            }
            return value;
        }
        if (!vector)
        {
            return changed ? Read(vm.KeyframeValueText, "KeyframeValueInput") : previous ?? (double)(vm.KeyframeValue ?? 0);
        }
        var value = previous?.Vector ?? new ScenePoint((double)(vm.KeyframeValue ?? 0), (double)(vm.KeyframeValueY ?? 0));
        var x = changedEffectFields.Overlaps(["KeyframeValue", "KeyframeValueText"]) ? Read(vm.KeyframeValueText, "KeyframeValueXInput") : value.X;
        var y = changedEffectFields.Overlaps(["KeyframeValueY", "KeyframeValueYText"]) ? Read(vm.KeyframeValueYText, "KeyframeValueYInput") : value.Y;
        return new ScenePoint(x, y);
    }

    private SceneColor ReadColorDraft(ColorDraft draft, string field)
    {
        if (!draft.TryCommit(out var value))
        {
            ViewModel.InvalidFieldKey = field;
            throw new InvalidDataException(draft.Error ?? WorkbenchText.Get("Fill"));
        }
        return value;
    }

    private double ReadNumber(string text, double original, string label, string fieldKey)
    {
        var number = RequiredNumber(text, label, fieldKey);
        return (decimal)number == (decimal)original ? original : number;
    }

    private double RequiredNumber(string text, string label, string fieldKey)
    {
        ViewModel.InvalidFieldKey = fieldKey;
        if (!decimal.TryParse(text, System.Globalization.NumberStyles.Float, InterfaceCulture, out var number))
        {
            throw new InvalidDataException(WorkbenchText.Get(label));
        }
        var (minimum, maximum) = fieldKey switch
        {
            "FontSizeInput" => (0.01m, 4096m),
            "StrokeWidthInput" => (0m, 4096m),
            "LayerWidthInput" or "LayerHeightInput" => (1m, 32768m),
            "PositionXInput" or "PositionYInput" => (-2000032768m, 2000032768m),
            "ScaleXInput" or "ScaleYInput" => (0.001m, 100m),
            "RotationInput" => (-36000m, 36000m),
            "OpacityInput" => (0m, 1m),
            "BlurInput" => (0m, 128m),
            "CrfInput" => (0m, 51m),
            "AudioBitrateInput" => (32m, 512m),
            "VideoBitrateInput" => (0.1m, 200m),
            _ => (decimal.MinValue, decimal.MaxValue)
        };
        if (number < minimum || number > maximum)
        {
            throw new InvalidDataException(WorkbenchText.Get(label));
        }
        return (double)number;
    }

    internal void CommitRow(SubtitleRow row)
    {
        if (!projectBusy && !updatingWorkbench && row.IsDirty)
        {
            TryCommitDrafts(false);
        }
    }

    internal void SetTextCaret(Guid id, int caret) => textCarets[id] = caret;

    private void OnStylePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "FontSize":
                ViewModel.Styles.FontSizeText = ViewModel.Styles.FontSize?.ToString(InterfaceCulture) ?? string.Empty;
                break;
            case "StrokeWidth":
                ViewModel.Styles.StrokeWidthText = ViewModel.Styles.StrokeWidth?.ToString(InterfaceCulture) ?? string.Empty;
                break;
        }

        if (!updatingWorkbench && e.PropertyName is { } name && styleDraftProperties.Contains(name))
        {
            stylesDirty = true;
            draftRevision++;
            FreezeDraftTarget();
        }
    }

    private void OnEffectPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case "LayerWidth":
                ViewModel.Effects.LayerWidthText = ViewModel.Effects.LayerWidth?.ToString(InterfaceCulture) ?? string.Empty;
                break;
            case "LayerHeight":
                ViewModel.Effects.LayerHeightText = ViewModel.Effects.LayerHeight?.ToString(InterfaceCulture) ?? string.Empty;
                break;
            case "PositionX":
                ViewModel.Effects.PositionXText = ViewModel.Effects.PositionX?.ToString(InterfaceCulture) ?? string.Empty;
                break;
            case "PositionY":
                ViewModel.Effects.PositionYText = ViewModel.Effects.PositionY?.ToString(InterfaceCulture) ?? string.Empty;
                break;
            case "ScaleX":
                ViewModel.Effects.ScaleXText = ViewModel.Effects.ScaleX?.ToString(InterfaceCulture) ?? string.Empty;
                break;
            case "ScaleY":
                ViewModel.Effects.ScaleYText = ViewModel.Effects.ScaleY?.ToString(InterfaceCulture) ?? string.Empty;
                break;
            case "Rotation":
                ViewModel.Effects.RotationText = ViewModel.Effects.Rotation?.ToString(InterfaceCulture) ?? string.Empty;
                break;
            case "Opacity":
                ViewModel.Effects.OpacityText = ViewModel.Effects.Opacity?.ToString(InterfaceCulture) ?? string.Empty;
                break;
            case "Blur":
                ViewModel.Effects.BlurText = ViewModel.Effects.Blur?.ToString(InterfaceCulture) ?? string.Empty;
                break;
            case "KeyframeValueY":
                ViewModel.Effects.KeyframeValueYText = ViewModel.Effects.KeyframeValueY?.ToString(InterfaceCulture) ?? string.Empty;
                break;
            case "KeyframeValue":
                ViewModel.Effects.KeyframeValueText = ViewModel.Effects.KeyframeValue?.ToString(InterfaceCulture) ?? string.Empty;
                break;
        }

        if (updatingWorkbench || e.PropertyName is null)
        {
            return;
        }

        if (effectDraftProperties.Contains(e.PropertyName))
        {
            effectsDirty = true;
            draftRevision++;
            FreezeDraftTarget();
            changedEffectFields.Add(e.PropertyName);
        }
        else if (e.PropertyName == "EditMode")
        {
            RefreshEditingPreview();
        }
        else if (e.PropertyName == "Property")
        {
            updatingWorkbench = true;
            try
            {
                ViewModel.Timeline.EffectProperty = ActiveProperty;
                RefreshKeyframeInspector();
                RefreshInspector();
            }
            finally
            {
                updatingWorkbench = false;
            }
        }
    }

    internal void RefreshDocument()
    {
        var previousUpdating = updatingWorkbench;
        updatingWorkbench = true;
        try
        {
            var document = editor.Snapshot;
            RefreshSubtitleTracks();
            Volatile.Write(ref previewState, CreatePreviewState());
            var oldRows = ViewModel.Subtitles.Rows.ToDictionary(value => value.Id);
            var rows = document.Subtitles.Select((line, index) =>
            {
                if (!oldRows.TryGetValue(line.Id, out var row) || row.Number != index + 1)
                {
                    return new SubtitleRow(line, index + 1);
                }

                if (!row.IsDirty || line != row.Original)
                {
                    row.Accept(line);
                }

                return row;
            }).ToArray();
            if (!ViewModel.Subtitles.Rows.SequenceEqual(rows))
            {
                ViewModel.Subtitles.Rows = rows;
            }

            if (!document.Subtitles.Any(value => value.Id == SelectedCueId))
            {
                SelectedCueId = null;
            }

            ViewModel.Subtitles.SelectedRow = rows.FirstOrDefault(value => value.Id == SelectedCueId);
            var layers = LayerItems(document.Layers, 0).ToArray();
            if (!ViewModel.Effects.Layers.SequenceEqual(layers))
            {
                ViewModel.Effects.Layers = layers;
            }

            if (!layers.Any(value => value.Id == SelectedLayerId))
            {
                SelectedLayerId = SelectedCueId is { } cueId ? Flatten(document.Layers).FirstOrDefault(layer => layer.SubtitleId == cueId)?.Id : null;
            }

            if (SelectedKeyTime is { } selectedTime && SelectedLayer?.Tracks.Any(track => track.Keyframes.Any(frame => frame.Time == selectedTime)) != true)
            {
                SelectedKeyTime = null;
            }
            ViewModel.Effects.SelectedItem = layers.FirstOrDefault(value => value.Id == SelectedLayerId);
            RefreshTitle();
            ViewModel.Timeline.Document = document;
            ViewModel.Timeline.SelectedCueId = SelectedCueId;
            ViewModel.Timeline.SelectedLayer = SelectedLayer;
            var selectedIds = ViewModel.Effects.SelectedIds.Where(id => layers.Any(layer => layer.Id == id)).ToArray();
            if (SelectedLayerId is { } primary && !selectedIds.Contains(primary))
            {
                selectedIds = [primary];
            }
            ViewModel.Effects.SelectedIds = selectedIds;
            ViewModel.Timeline.SelectedLayerIds = selectedIds;
            ViewModel.Effects.Document = document;
            ViewModel.Effects.SelectedLayer = SelectedLayer;
            SyncCurrentTrackForSelection();
            RefreshInspector();
            ViewModel.Styles.CanApplyPreset = SelectedCue is not null && !projectBusy && !closing && ViewModel.Styles.SelectedPreset is not null;
            Tick();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            updatingWorkbench = previousUpdating;
        }
    }

    private void RefreshInspector()
    {
        var layer = SelectedLayer;
        var cue = layer?.SubtitleId is { } id ? editor.Snapshot.Subtitles.First(value => value.Id == id) : null;
        var style = cue?.Style ?? new();
        var placement = ResolvePlacement(editor.Snapshot, layer);
        var vm = ViewModel.Styles;
        vm.HasCue = cue is not null;
        if (!stylesDirty)
        {
            vm.FontFamily = style.FontFamily;
            vm.FontDraft = style.FontFamily;
            vm.FontSize = (decimal)style.FontSize;
            vm.StrokeWidth = (decimal)(layer is null ? 0 : InspectorValue(layer, AnimationProperty.STROKE_WIDTH, cue is null ? layer.StrokeWidth : style.StrokeWidth));
            vm.FillDraft.Load(layer is null ? SceneColor.White : InspectorColor(layer, cue is null ? layer.Fill : style.Fill, false));
            vm.StrokeDraft.Load(layer is null ? SceneColor.Black : InspectorColor(layer, cue is null ? layer.Stroke : style.Stroke, true));
            vm.Bold = style.Bold;
            vm.Italic = style.Italic;
            vm.Alignment = Array.IndexOf(alignments, style.Alignment);
            var positionGeometry = placement.Geometry;
            if (positionGeometry is not null && layer is not null)
            {
                positionGeometry = positionGeometry with
                {
                    Transform = layer.Transform with
                    {
                        Scale = InspectorVector(layer, AnimationProperty.SCALE, layer.Transform.Scale),
                        Rotation = InspectorValue(layer, AnimationProperty.ROTATION, layer.Transform.Rotation)
                    }
                };
            }
            vm.Position.Load(style, placement.Position, placement.BasePosition is not null, positionGeometry);
        }

        var effects = ViewModel.Effects;
        if (!effectsDirty)
        {
            var transform = layer?.Transform ?? new();
            if (layer is not null)
            {
                transform = transform with
                {
                    Position = InspectorVector(layer, AnimationProperty.POSITION, transform.Position),
                    Scale = InspectorVector(layer, AnimationProperty.SCALE, transform.Scale),
                    Rotation = InspectorValue(layer, AnimationProperty.ROTATION, transform.Rotation)
                };
            }
            effects.CanEditPosition = layer is not null && placement.BasePosition is not null;
            effects.PositionX = placement.BasePosition is { } baseX ? (decimal)(baseX.X + transform.X) : null;
            effects.PositionY = placement.BasePosition is { } baseY ? (decimal)(baseY.Y + transform.Y) : null;
            effects.ScaleX = (decimal)transform.ScaleX;
            effects.ScaleY = (decimal)transform.ScaleY;
            effects.Rotation = (decimal)transform.Rotation;
            effects.Opacity = (decimal)(layer is null ? 1 : InspectorValue(layer, AnimationProperty.OPACITY, layer.Opacity));
            effects.Blur = (decimal)(layer is null ? 0 : InspectorValue(layer, AnimationProperty.BLUR, layer.Blur));
            effects.LayerWidth = (decimal)(layer?.Shape?.Width ?? layer?.Image?.Width ?? 300);
            effects.LayerHeight = (decimal)(layer?.Shape?.Height ?? layer?.Image?.Height ?? 180);
            effects.CanResizeLayer = layer?.Shape is not null || layer?.Image is not null;
            effects.LayerStart = layer is null ? string.Empty : TimelineTimeText.Format(layer.Start);
            effects.LayerEnd = layer is null ? string.Empty : TimelineTimeText.Format(layer.End);
            effects.Blend = (int)(layer?.Blend ?? BlendMode.NORMAL);
            effects.InvertMask = layer?.Mask?.Inverted ?? false;
            effects.OrientPath = layer?.MotionPath?.OrientToPath ?? false;
            RefreshKeyframeInspector();
        }

        RefreshEditingTargetLabel();
        effectScripts?.RefreshChoices();
    }

    private Rendering.LayerPlacementResolution ResolvePlacement(ProjectDocument document, ProjectLayer? layer)
    {
        var result = layerPlacement.Resolve(document, projectDirectory, layer);
        if (result.Error is { } error)
        {
            placementDiagnostic = new InvalidDataException($"{WorkbenchText.Get("SubtitlePositionUnavailable")}: {error.Message}", error);
            SetDiagnosticError("Subtitle placement", placementDiagnostic);
            ShowError(placementDiagnostic, false);
        }
        else if (placementDiagnostic is not null)
        {
            var source = placementDiagnostic.InnerException;
            if (ReferenceEquals(LastError, placementDiagnostic) ||
                source is not null && LastError is { } previous && previous.GetType() == source.GetType() && previous.Message == source.Message)
            {
                LastError = null;
                ViewModel.Error = null;
            }

            placementDiagnostic = null;
            SetDiagnosticError("Subtitle placement", null);
        }

        return result;
    }

    private void RestorePlacementDiagnostic()
    {
        if (LastError is null && placementDiagnostic is { } error)
        {
            ShowError(error, false);
        }
    }

    internal void SelectCue(Guid id)
    {
        if (updatingWorkbench || projectBusy || id == SelectedCueId && SelectedLayer?.SubtitleId == id)
        {
            return;
        }

        if (!TryCommitDrafts())
        {
            RefreshDocument();
            return;
        }

        ViewModel.CancelGestures();
        SelectedCueId = id;
        ViewModel.Effects.SelectedIds = [];
        SelectedLayerId = Flatten(editor.Snapshot.Layers).FirstOrDefault(layer => layer.SubtitleId == id)?.Id;
        SelectedKeyTime = null;
        ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
        RefreshDocument();
    }

    internal void SelectLayer(Guid id, Guid[] selectedIds)
    {
        if (updatingWorkbench || projectBusy)
        {
            return;
        }

        if (id != SelectedLayerId && !TryCommitDrafts())
        {
            RefreshDocument();
            return;
        }

        ViewModel.CancelGestures();
        SelectedLayerId = id;
        ViewModel.Effects.SelectedIds = selectedIds;
        SelectedKeyTime = null;
        ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
        if (SelectedLayer?.SubtitleId is { } cueId)
        {
            SelectedCueId = cueId;
        }

        RefreshDocument();
    }

    private static IEnumerable<LayerListItem> LayerItems(ImmutableArray<ProjectLayer> layers, int depth)
    {
        foreach (var layer in layers)
        {
            yield return new(layer.Id, new string(' ', depth * 3) + (layer.Name == "Subtitle" ? WorkbenchText.Get("Subtitles") : layer.Name));
            foreach (var child in LayerItems(layer.Children, depth + 1))
            {
                yield return child;
            }
        }
    }
}
