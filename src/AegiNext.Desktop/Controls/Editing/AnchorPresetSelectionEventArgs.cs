using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Controls;

/// <summary>九宫格发出的纯锚点选择与修饰键语义。</summary>
public sealed class AnchorPresetSelectionEventArgs(ScenePoint anchor, bool setPivot, bool resetOffset) : EventArgs
{
    public ScenePoint Anchor { get; } = anchor;
    public bool SetPivot { get; } = setPivot;
    public bool ResetOffset { get; } = resetOffset;
}
