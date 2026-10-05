using AegiNext.Core.Projects;

namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>将字幕、独立轨道与合成层作为一次可撤销事务导入。</summary>
    public void ImportSubtitleLines(IEnumerable<SubtitleLine> lines, string name)
    {
        Apply("Import subtitle format", document => ProjectEditingOperations.ImportSubtitleLines(document, lines, name));
    }
}
