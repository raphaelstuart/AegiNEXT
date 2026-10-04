using AegiNext.Core.Timing;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;

namespace AegiNext.Desktop.Workspace;

/// <summary>工作台共享的场景选择与暂存手势目标，不持有视图或渲染资源。</summary>
internal sealed class SceneEditingState
{
    internal Guid? LayerId { get; set; }
    internal Guid? CueId { get; set; }
    internal Guid[] SelectedLayerIds { get; set; } = [];
    internal AnimationProperty Property { get; set; }
    internal CanvasEditMode Mode { get; set; }
    internal MediaTime? KeyframeTime { get; set; }
    internal AnimationEditTarget? DraftTarget { get; set; }
    internal AnimationEditTarget? GestureTarget { get; set; }
}
