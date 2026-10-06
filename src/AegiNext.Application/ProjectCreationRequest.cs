namespace AegiNext.Application;

/// <summary>新工程的跨平台名称与本机父目录。</summary>
public sealed record ProjectCreationRequest(string Name, string ParentDirectory);
