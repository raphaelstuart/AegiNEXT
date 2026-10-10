using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Workspace;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Panels.SubtitleDetails;

internal sealed partial class SubtitleDetailsPanelView
{
    private NumericDragEditSnapshot? numericDragSnapshot;
    private Guid? numericDragClipId;
    private KaraokeVisualState? numericDragState;
    private int numericDragSelectionStart;
    private int numericDragSelectionEnd;
    private object? numericDragDraft;

    private void BindNumericDrags()
    {
        AddHandler(NumericDragLabel.DragStartedEvent, (_, _) =>
        {
            ++styleFocusRevision;
            formattingPointerActive = true;
            formattingFocusPending = false;
            numericDragSnapshot = new(session);
            numericDragClipId = coordinator.SelectedClipId;
            numericDragState = coordinator.VisualState;
            numericDragSelectionStart = selectionStart;
            numericDragSelectionEnd = selectionEnd;
            numericDragDraft = styleFields.DataContext;
        }, RoutingStrategies.Bubble);
        AddHandler(NumericDragLabel.DragCompletedEvent, (_, args) =>
        {
            var matches = NumericDragTargetMatches();
            numericDragSnapshot = null;
            ++styleFocusRevision;
            formattingPointerActive = formattingFocusPending = false;
            if (args.Changed && matches && args.Input != creationStart && args.Input != creationEnd)
            {
                coordinator.TryCommit();
            }
        }, RoutingStrategies.Bubble);
        AddHandler(NumericDragLabel.DragCanceledEvent, (_, _) =>
        {
            numericDragSnapshot = null;
            ++styleFocusRevision;
            formattingPointerActive = formattingFocusPending = false;
        }, RoutingStrategies.Bubble);
        session.NumericGestureCancellationRequested += OnNumericGestureCancellation;
    }

    private bool NumericDragTargetMatches()
    {
        return numericDragSnapshot?.Matches(session) == true && numericDragClipId == coordinator.SelectedClipId &&
            numericDragState == coordinator.VisualState && numericDragSelectionStart == selectionStart &&
            numericDragSelectionEnd == selectionEnd && ReferenceEquals(numericDragDraft, styleFields.DataContext);
    }

    private void OnNumericGestureCancellation(object? sender, EventArgs args) => CancelNumericDrags();

    private void CancelNumericDrags()
    {
        foreach (var label in this.GetVisualDescendants().OfType<NumericDragLabel>())
        {
            label.CancelDrag();
        }
        numericDragSnapshot = null;
    }
}
