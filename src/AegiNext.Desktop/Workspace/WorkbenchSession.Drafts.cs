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
        "FontFamily", "FontDraft", "FontSize", "FontSizeText", "StrokeWidth", "StrokeWidthText", "Fill", "Stroke", "Bold", "Italic", "Alignment"
    ];
    private static readonly HashSet<string> effectDraftProperties =
    [
        "LayerName", "LayerStart", "LayerEnd", "LayerWidth", "LayerWidthText", "LayerHeight", "LayerHeightText", "PositionX", "PositionXText", "PositionY", "PositionYText", "ScaleX", "ScaleXText", "ScaleY", "ScaleYText",
        "Rotation", "RotationText", "Opacity", "OpacityText", "Blur", "BlurText", "Blend", "InvertMask", "OrientPath", "KeyframeValue", "KeyframeValueText", "Interpolation"
    ];

    internal bool TryCommitDrafts()
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
                    var strokeWidth = ReadNumber(vm.StrokeWidthText, line.Style.StrokeWidth, "StrokeWidth", "StrokeWidthInput");
                    if (fontSize <= 0 || strokeWidth < 0 || string.IsNullOrWhiteSpace(vm.FontFamily))
                    {
                        throw new InvalidDataException(WorkbenchText.Get("Font") + ": " + WorkbenchText.Get("Size"));
                    }

                    var familyChanged = family != line.Style.FontFamily;
                    var style = line.Style with
                    {
                        FontFamily = family,
                        FontAssetId = familyChanged ? null : line.Style.FontAssetId,
                        FontSize = fontSize,
                        StrokeWidth = strokeWidth,
                        Fill = PreserveColor(vm.Fill, line.Style.Fill),
                        Stroke = PreserveColor(vm.Stroke, line.Style.Stroke),
                        Bold = vm.Bold == true,
                        Italic = vm.Italic == true,
                        Alignment = alignments[Math.Clamp(vm.Alignment, 0, alignments.Length - 1)]
                    };
                    prepared = WorkspaceDraftOperations.UpdateSubtitle(prepared, id, cue => cue with { Style = style });
                }
                else
                {
                    prepared = WorkspaceDraftOperations.UpdateLayer(prepared, selected.Id, layer => layer with
                    {
                        Fill = PreserveColor(vm.Fill, layer.Fill),
                        Stroke = PreserveColor(vm.Stroke, layer.Stroke),
                        StrokeWidth = ReadNumber(vm.StrokeWidthText, layer.StrokeWidth, "StrokeWidth", "StrokeWidthInput")
                    });
                }
            }

            if (effectsDirty && selected is not null)
            {
                var vm = ViewModel.Effects;
                ViewModel.InvalidPanelId = "effects";
                var keyframeValue = RequiredNumber(vm.KeyframeValueText, "Property", "KeyframeValueInput");
                if (keyframeValue < (double)vm.KeyframeMinimum || keyframeValue > (double)vm.KeyframeMaximum)
                {
                    throw new InvalidDataException(WorkbenchText.Get("Property"));
                }
                ViewModel.InvalidFieldKey = "LayerStartInput";
                var start = vm.LayerStart == TimelineTimeText.Format(selected.Start) ? selected.Start : TimelineTimeText.Parse(vm.LayerStart);
                ViewModel.InvalidFieldKey = "LayerEndInput";
                var end = vm.LayerEnd == TimelineTimeText.Format(selected.End) ? selected.End : TimelineTimeText.Parse(vm.LayerEnd);
                ViewModel.InvalidFieldKey = "LayerNameInput";
                if (selected.SubtitleId is { } id && (start != selected.Start || end != selected.End))
                {
                    prepared = WorkspaceDraftOperations.UpdateSubtitle(prepared, id, line => line with { Start = start, End = end });
                }

                prepared = WorkspaceDraftOperations.UpdateLayer(prepared, selected.Id, layer => layer with
                {
                    Name = vm.LayerName ?? string.Empty,
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
                    Transform = layer.Transform with
                    {
                        X = ReadNumber(vm.PositionXText, layer.Transform.X, "PositionX", "PositionXInput"), Y = ReadNumber(vm.PositionYText, layer.Transform.Y, "PositionY", "PositionYInput"),
                        ScaleX = ReadNumber(vm.ScaleXText, layer.Transform.ScaleX, "ScaleX", "ScaleXInput"), ScaleY = ReadNumber(vm.ScaleYText, layer.Transform.ScaleY, "ScaleY", "ScaleYInput"),
                        Rotation = ReadNumber(vm.RotationText, layer.Transform.Rotation, "Rotation", "RotationInput")
                    },
                    Opacity = ReadNumber(vm.OpacityText, layer.Opacity, "Opacity", "OpacityInput"), Blur = ReadNumber(vm.BlurText, layer.Blur, "Blur", "BlurInput"),
                    Blend = (BlendMode)vm.Blend,
                    MotionPath = layer.MotionPath is { } path ? path with { OrientToPath = vm.OrientPath == true } : null,
                    Mask = layer.Mask is { } mask ? mask with { Inverted = vm.InvertMask == true } : null
                });
                if (SelectedKeyframe is { } frame)
                {
                    prepared = WorkspaceDraftOperations.SetKeyframe(prepared, selected.Id, ActiveProperty, frame with
                    {
                        Value = keyframeValue,
                        Interpolation = (KeyframeInterpolation)vm.Interpolation
                    });
                }
            }

            ProjectValidator.Validate(prepared);
            ViewModel.InvalidPanelId = "export";
            var crf = RequiredNumber(ViewModel.Export.CrfText, "Quality", "CrfInput");
            if (crf is < 0 or > 51 || crf != Math.Truncate(crf))
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
            ViewModel.Export.AudioBitrate = (decimal)bitrate;
            var originalStylesDirty = stylesDirty;
            var originalEffectsDirty = effectsDirty;
            stylesDirty = false;
            effectsDirty = false;
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
                throw;
            }

            ViewModel.InvalidPanelId = null;
            ViewModel.InvalidFieldKey = null;
            ViewModel.Subtitles.ValidationError = null;
            ViewModel.Subtitles.InvalidRowId = null;
            return true;
        }
        catch (Exception error)
        {
            ViewModel.Subtitles.InvalidRowId = invalidRow;
            ViewModel.Subtitles.ValidationError = error.Message;
            ShowError(error);
            ViewModel.FocusDraftError();
            return false;
        }
    }

    private static SceneColor PreserveColor(Avalonia.Media.Color draft, SceneColor original) =>
        draft == SceneColorConversion.ToColor(original) ? original : SceneColorConversion.FromColor(draft);

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
            "PositionXInput" or "PositionYInput" => (-32768m, 32768m),
            "ScaleXInput" or "ScaleYInput" => (0.001m, 100m),
            "RotationInput" => (-36000m, 36000m),
            "OpacityInput" => (0m, 1m),
            "BlurInput" => (0m, 128m),
            "CrfInput" => (0m, 51m),
            "AudioBitrateInput" => (32m, 512m),
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
            TryCommitDrafts();
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
        }
        else if (e.PropertyName == "Property")
        {
            ViewModel.Timeline.EffectProperty = ActiveProperty;
            ViewModel.Timeline.ShowEffects = true;
            ClearKeyframeSelection();
            RefreshKeyframeInspector();
        }
    }

    internal void RefreshDocument()
    {
        var previousUpdating = updatingWorkbench;
        updatingWorkbench = true;
        try
        {
            var document = editor.Snapshot;
            Volatile.Write(ref previewState, new(document, projectDirectory));
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

            if (!document.Subtitles.Any(value => value.Id == selectedCueId))
            {
                selectedCueId = document.Subtitles.FirstOrDefault()?.Id;
            }

            ViewModel.Subtitles.SelectedRow = rows.FirstOrDefault(value => value.Id == selectedCueId);
            var layers = LayerItems(document.Layers, 0).ToArray();
            if (!ViewModel.Effects.Layers.SequenceEqual(layers))
            {
                ViewModel.Effects.Layers = layers;
            }

            if (!layers.Any(value => value.Id == selectedLayerId))
            {
                selectedLayerId = Flatten(document.Layers).FirstOrDefault(layer => layer.SubtitleId == selectedCueId)?.Id ?? layers.FirstOrDefault()?.Id;
            }

            ViewModel.Effects.SelectedItem = layers.FirstOrDefault(value => value.Id == selectedLayerId);
            ViewModel.Title = (document.Name == "Untitled" ? WorkbenchText.Get("Untitled") : document.Name) +
                              (editor.HasUnsavedChanges ? " •" : string.Empty) + " — AegiNext";
            ViewModel.Timeline.Document = document;
            ViewModel.Timeline.SelectedCueId = selectedCueId;
            ViewModel.Timeline.SelectedLayer = SelectedLayer;
            ViewModel.Effects.Document = document;
            ViewModel.Effects.SelectedLayer = SelectedLayer;
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
        var vm = ViewModel.Styles;
        vm.HasCue = cue is not null;
        if (!stylesDirty)
        {
            vm.FontFamily = style.FontFamily;
            vm.FontDraft = style.FontFamily;
            vm.FontSize = (decimal)style.FontSize;
            vm.StrokeWidth = (decimal)(cue is null ? layer?.StrokeWidth ?? 0 : style.StrokeWidth);
            vm.Fill = SceneColorConversion.ToColor(cue is null ? layer?.Fill ?? SceneColor.White : style.Fill);
            vm.Stroke = SceneColorConversion.ToColor(cue is null ? layer?.Stroke ?? SceneColor.Black : style.Stroke);
            vm.Bold = style.Bold;
            vm.Italic = style.Italic;
            vm.Alignment = Array.IndexOf(alignments, style.Alignment);
        }

        var effects = ViewModel.Effects;
        if (!effectsDirty)
        {
            var transform = layer?.Transform ?? new();
            effects.PositionX = (decimal)transform.X;
            effects.PositionY = (decimal)transform.Y;
            effects.ScaleX = (decimal)transform.ScaleX;
            effects.ScaleY = (decimal)transform.ScaleY;
            effects.Rotation = (decimal)transform.Rotation;
            effects.Opacity = (decimal)(layer?.Opacity ?? 1);
            effects.Blur = (decimal)(layer?.Blur ?? 0);
            effects.LayerWidth = (decimal)(layer?.Shape?.Width ?? layer?.Image?.Width ?? 300);
            effects.LayerHeight = (decimal)(layer?.Shape?.Height ?? layer?.Image?.Height ?? 180);
            effects.CanResizeLayer = layer?.Shape is not null || layer?.Image is not null;
            effects.LayerName = layer?.Name;
            effects.LayerStart = layer is null ? string.Empty : TimelineTimeText.Format(layer.Start);
            effects.LayerEnd = layer is null ? string.Empty : TimelineTimeText.Format(layer.End);
            effects.Blend = (int)(layer?.Blend ?? BlendMode.NORMAL);
            effects.InvertMask = layer?.Mask?.Inverted ?? false;
            effects.OrientPath = layer?.MotionPath?.OrientToPath ?? false;
            RefreshKeyframeInspector();
        }

        var names = editor.Snapshot.Presets.Select(value => value.Name).ToArray();
        if (!effects.Presets.SequenceEqual(names))
        {
            effects.Presets = names;
        }

        effects.Preset = names.Length == 0 ? -1 : Math.Clamp(effects.Preset, 0, names.Length - 1);
    }

    internal void SelectCue(Guid id)
    {
        if (updatingWorkbench || projectBusy || id == selectedCueId)
        {
            return;
        }

        if (!TryCommitDrafts())
        {
            RefreshDocument();
            return;
        }

        selectedCueId = id;
        selectedLayerId = Flatten(editor.Snapshot.Layers).FirstOrDefault(layer => layer.SubtitleId == id)?.Id;
        selectedKeyTime = null;
        ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
        RefreshDocument();
    }

    internal void SelectLayer(Guid id, Guid[] selectedIds)
    {
        if (updatingWorkbench || projectBusy)
        {
            return;
        }

        if (id != selectedLayerId && !TryCommitDrafts())
        {
            RefreshDocument();
            return;
        }

        selectedLayerId = id;
        ViewModel.Effects.SelectedIds = selectedIds;
        selectedKeyTime = null;
        ViewModel.Effects.EditMode = CanvasEditMode.POSITION;
        if (SelectedLayer?.SubtitleId is { } cueId)
        {
            selectedCueId = cueId;
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
