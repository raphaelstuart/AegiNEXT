using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Editing;

/// <summary>颜色草稿经显式用户操作验证后提交的线性色。</summary>
public sealed class ColorDraftCommittedEventArgs(SceneColor value) : EventArgs
{
    public SceneColor Value { get; } = value;
}
