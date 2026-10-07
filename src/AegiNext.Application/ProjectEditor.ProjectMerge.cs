namespace AegiNext.Application;

public sealed partial class ProjectEditor
{
    /// <summary>将多个已准备资源的工程作为独立副本合入当前快照，整批成功仅产生一次撤销事务。</summary>
    public ProjectMergeResult MergeProjects(IReadOnlyList<ProjectMergeSource> sources)
    {
        ProjectMergeResult? result = null;
        Apply("Merge projects", document =>
        {
            result = ProjectEditingOperations.MergeProjects(document, sources);
            return result.Document;
        });
        return result! with { Document = Snapshot };
    }
}
