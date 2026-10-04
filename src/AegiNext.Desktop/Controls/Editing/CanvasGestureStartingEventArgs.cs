namespace AegiNext.Desktop.Controls;

/// <summary>开始场景手势前验证并固定编辑目标；无效草稿可取消手势。</summary>
public sealed class CanvasGestureStartingEventArgs : EventArgs
{
    public bool Cancel { get; set; }
}
