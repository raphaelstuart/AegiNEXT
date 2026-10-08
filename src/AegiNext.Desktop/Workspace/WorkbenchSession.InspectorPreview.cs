using System.Globalization;
using AegiNext.Core.Projects;
using Avalonia.Threading;

namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    private ProjectDocument? inspectorPreview;
    private ProjectDocument? inspectorPreviewSource;
    private Guid? inspectorPreviewLayerId;
    private bool inspectorPreviewQueued;
    private int inspectorPreviewRevision;

    internal ProjectDocument PreviewDocument
    {
        get
        {
            var document = inspectorPreview is { } preview && ReferenceEquals(inspectorPreviewSource, DocumentSnapshot) &&
                inspectorPreviewLayerId == SelectedLayerId ? preview : DocumentSnapshot;
            document = Details?.OverlayPreview(document) ?? document;
            document = MaskEditing?.Overlay(document) ?? document;
            return OverlayTimingPreview(ViewModel.Effects.OverlayOperationDraft(document));
        }
    }

    private void QueueInspectorPreview(bool supersedePending = false)
    {
        if (supersedePending)
        {
            inspectorPreviewRevision++;
            inspectorPreviewQueued = false;
        }

        if (inspectorPreviewQueued || IsUpdating || closing || IsProjectBusy)
        {
            return;
        }

        inspectorPreviewQueued = true;
        var revision = inspectorPreviewRevision;
        var source = DocumentSnapshot;
        var layerId = SelectedLayerId;
        var target = SceneEditing.DraftTarget;
        Dispatcher.UIThread.Post(() =>
        {
            if (revision != inspectorPreviewRevision)
            {
                return;
            }
            inspectorPreviewQueued = false;
            if (closing || IsProjectBusy || IsUpdating || layerId != SelectedLayerId || target != SceneEditing.DraftTarget ||
                !ReferenceEquals(source, DocumentSnapshot))
            {
                return;
            }

            var invalidPanel = ViewModel.InvalidPanelId;
            var invalidField = ViewModel.InvalidFieldKey;
            try
            {
                var candidate = PrepareInspectorDrafts(source, true);
                ProjectValidator.Validate(candidate);
                inspectorPreview = candidate;
                inspectorPreviewSource = source;
                inspectorPreviewLayerId = layerId;
                RefreshEditingPreview();
            }
            catch (Exception error) when (error is InvalidDataException or ArgumentException or InvalidOperationException or OverflowException or FormatException)
            {
                // Invalid raw drafts retain the most recent valid scene until the user repairs or restores the field.
            }
            finally
            {
                ViewModel.InvalidPanelId = invalidPanel;
                ViewModel.InvalidFieldKey = invalidField;
            }
        }, DispatcherPriority.Background);
    }

    private void ClearInspectorPreview()
    {
        inspectorPreviewRevision++;
        inspectorPreviewQueued = false;
        inspectorPreview = null;
        inspectorPreviewSource = null;
        inspectorPreviewLayerId = null;
    }

    private string SynchronizeNumericText(string text, decimal? value)
    {
        if (!IsUpdating && (value is null && string.IsNullOrWhiteSpace(text) ||
            decimal.TryParse(text, NumberStyles.Float, InterfaceCulture, out var parsed) && parsed == value))
        {
            return text;
        }
        return value?.ToString(InterfaceCulture) ?? string.Empty;
    }
}
