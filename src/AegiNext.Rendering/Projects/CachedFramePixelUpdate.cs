namespace AegiNext.Rendering.Projects;

/// <summary>透明前景缓存版本及目标写入状态；空前景通过状态表示，不依赖目标缓冲内容。</summary>
public readonly record struct CachedFramePixelUpdate(ulong Revision, bool Updated, bool Empty);
