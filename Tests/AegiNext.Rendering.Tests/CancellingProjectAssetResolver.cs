using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

internal sealed class CancellingProjectAssetResolver(IProjectAssetResolver source, CancellationTokenSource cancellation) : IProjectAssetResolver
{
    internal int OpenCount { get; private set; }

    /// <summary>模拟资源读取完成时由更新的预览请求取消本次合成。</summary>
    public Stream Open(ProjectAsset asset)
    {
        var stream = source.Open(asset);
        OpenCount++;
        if (OpenCount == 1)
        {
            cancellation.Cancel();
        }
        return stream;
    }
}
