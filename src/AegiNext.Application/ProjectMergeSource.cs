using AegiNext.Core.Projects;

namespace AegiNext.Application;

/// <summary>待合并工程的不可变快照、来源名称与资源解析目录；合并操作本身不访问文件系统。</summary>
public sealed record ProjectMergeSource(ProjectDocument Document, string Name, string Directory);
