using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Panels.Effects;

internal sealed partial class EffectsPanelViewModel
{
    private readonly Dictionary<string, string> operationOriginals = [];
    private bool loadingOperation;
    private ProjectDocument? operationSource;
    private Guid? operationLayer;
    private AnimationTrackTarget operationTarget;
    private Guid? operationIdentity;

    private IEnumerable<(string Name, NumericValueDraft Draft)> OperationFields =>
    [
        ("OperationStartInput", OperationStart), ("OperationEndInput", OperationEnd),
        ("OperationValueXInput", OperationValueX), ("OperationValueYInput", OperationValueY),
        ("OperationAccelerationInput", OperationAcceleration), ("OperationOrderInput", OperationOrder)
    ];
    internal bool HasOperationDraft => OperationFields.Any(draftField => operationOriginals.TryGetValue(draftField.Name, out var original) && draftField.Draft.RawText != original);

    private void InitializeOperationDrafts()
    {
        foreach (var field in OperationFields)
        {
            field.Draft.PropertyChanged += (_, e) =>
            {
                if (loadingOperation || session.IsUpdating || e.PropertyName != nameof(NumericValueDraft.RawText))
                {
                    return;
                }
                session.NotifyTaskInputChanged();
                operationSource ??= session.DocumentSnapshot;
                operationLayer ??= SelectedLayer?.Id;
                operationTarget = Target;
                operationIdentity ??= SelectedOperation?.Id;
                session.RefreshMaskPreview();
            };
        }
    }

    public bool RestoreOperationField(string? name)
    {
        name = name == "OperationValueInput" ? "OperationValueXInput" : name;
        var field = OperationFields.FirstOrDefault(field => field.Name == name);
        if (field.Draft is null || !operationOriginals.TryGetValue(field.Name, out var original))
        {
            return false;
        }
        loadingOperation = true;
        try
        {
            field.Draft.RawText = original;
        }
        finally
        {
            loadingOperation = false;
        }
        session.RefreshMaskPreview();
        return true;
    }

    internal ProjectDocument PrepareOperationDraft(ProjectDocument document)
    {
        if (!HasOperationDraft)
        {
            return document;
        }
        if (!ReferenceEquals(operationSource, session.DocumentSnapshot) || operationLayer != SelectedLayer?.Id)
        {
            throw new InvalidOperationException(Localization.Get("Workbench.SubtitleDraftConflict"));
        }
        var layer = document.Layers.Single(layer => layer.Id == operationLayer);
        var track = layer.Tracks.Single(track => track.Target == operationTarget);
        var operation = track.Transforms.Single(operation => operation.Id == operationIdentity);
        double Read(string key, NumericValueDraft field)
        {
            if (double.TryParse(field.RawText, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.CurrentCulture, out var value) && double.IsFinite(value))
            {
                return value;
            }
            session.ViewModel.InvalidPanelId = "effects";
            session.ViewModel.InvalidFieldKey = key;
            throw new InvalidDataException(Localization.Get("Workbench.InvalidValue"));
        }
        var start = Read("OperationStartInput", OperationStart);
        var end = Read("OperationEndInput", OperationEnd);
        var acceleration = Read("OperationAccelerationInput", OperationAcceleration);
        var order = Read("OperationOrderInput", OperationOrder);
        if (end < start || acceleration < 0 || order != Math.Truncate(order) || order < 1 || order > track.Transforms.Length)
        {
            throw new InvalidDataException(Localization.Get("Workbench.InvalidValue"));
        }
        var x = Read("OperationValueXInput", OperationValueX);
        AnimationValue value = operation.Value.IsVector ? new ScenePoint(x, Read("OperationValueYInput", OperationValueY)) : x;
        var changed = operation with
        {
            Start = new MediaTime((long)Math.Round(start * 1000000), 1000000),
            End = new MediaTime((long)Math.Round(end * 1000000), 1000000), Value = value, Acceleration = acceleration
        };
        var operations = track.Transforms.Remove(operation).Insert((int)order - 1, changed);
        return WorkspaceDraftOperations.UpdateLayer(document, layer.Id, candidate => candidate with
        {
            Tracks = candidate.Tracks.SetItem(candidate.Tracks.IndexOf(track), track with { Transforms = operations })
        });
    }

    internal ProjectDocument OverlayOperationDraft(ProjectDocument document)
    {
        try
        {
            var result = PrepareOperationDraft(document);
            if (!ReferenceEquals(result, document))
            {
                ProjectValidator.Validate(result);
            }
            return result;
        }
        catch (Exception error) when (error is InvalidDataException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return document;
        }
    }

    internal void RebindRelocatedSource(ProjectDocument document)
    {
        if (operationSource is not null)
        {
            operationSource = document;
        }
    }

    internal void AcceptOperationDraft()
    {
        operationSource = null;
        operationLayer = null;
        operationIdentity = null;
        operationOriginals.Clear();
    }
}
