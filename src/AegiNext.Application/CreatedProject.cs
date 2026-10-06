using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>已原子提交到独立工程目录的新工程。</summary>
public sealed record CreatedProject(string Path, ProjectDocument Document);
