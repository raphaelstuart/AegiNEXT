namespace AegiNext.Application;

/// <summary>区分工程内容变化与保存点变化的编辑器事件数据。</summary>
public sealed class ProjectEditorChangedEventArgs(ProjectEditorChangeKind kind) : EventArgs
{
    public ProjectEditorChangeKind Kind { get; } = kind;
}
