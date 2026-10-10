using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Workspace;

internal sealed class NumericDragEditSnapshot(WorkbenchSession session)
{
    private readonly ProjectDocument document = session.DocumentSnapshot;
    private readonly Guid? layerId = session.SelectedLayerId;
    private readonly Guid[] selectedIds = [.. session.SceneEditing.SelectedLayerIds];
    private readonly MediaTime position = session.EditingPosition;
    private readonly AnimationTrackTarget target = session.SceneEditing.Target;
    private readonly Guid? nodeId = session.SceneEditing.MaskNodeId;
    private readonly Guid? contourId = session.SceneEditing.MaskContourId;

    internal bool Matches(WorkbenchSession session)
    {
        return ReferenceEquals(document, session.DocumentSnapshot) && layerId == session.SelectedLayerId &&
            selectedIds.AsSpan().SequenceEqual(session.SceneEditing.SelectedLayerIds) && position == session.EditingPosition &&
            target == session.SceneEditing.Target && nodeId == session.SceneEditing.MaskNodeId && contourId == session.SceneEditing.MaskContourId;
    }
}
