using AegiNext.Core.Projects;

namespace AegiNext.Rendering.Projects;

/// <summary>为不可变工程提供可读资源；返回的流由渲染器关闭。</summary>
public interface IProjectAssetResolver
{
    /// <summary>打开指定资源，不以工作目录推断工程位置。</summary>
    Stream Open(ProjectAsset asset);
}
