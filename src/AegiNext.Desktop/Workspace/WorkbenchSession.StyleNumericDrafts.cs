namespace AegiNext.Desktop.Workspace;

internal sealed partial class WorkbenchSession
{
    internal bool RestoreStyleNumericDraftField(string fieldKey)
    {
        if (SelectedLayer?.SubtitleId is null || SelectedCue is not { } cue || updatingWorkbench || closing)
        {
            return false;
        }
        var value = fieldKey switch
        {
            "LineHeightInput" => cue.Style.LineHeight,
            "ShadowXInput" => cue.Style.ShadowOffset.X,
            "ShadowYInput" => cue.Style.ShadowOffset.Y,
            "ShadowBlurInput" => cue.Style.ShadowBlur,
            _ => (double?)null
        };
        if (value is not { } number)
        {
            return false;
        }
        var vm = ViewModel.Styles;
        updatingWorkbench = true;
        try
        {
            vm.LoadStyleNumber(fieldKey, number, InterfaceCulture);
            draftRevision++;
            if (ViewModel.InvalidPanelId == "styles" && ViewModel.InvalidFieldKey == fieldKey)
            {
                ViewModel.InvalidPanelId = null;
                ViewModel.InvalidFieldKey = null;
                lastDraftDiagnostic = null;
                lastDraftFocus = null;
            }
        }
        finally
        {
            updatingWorkbench = false;
        }
        QueueInspectorPreview(true);
        return true;
    }
}
