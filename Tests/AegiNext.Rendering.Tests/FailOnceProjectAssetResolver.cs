using AegiNext.Core.Projects;
using AegiNext.Rendering.Projects;

namespace AegiNext.Rendering.Tests;

internal sealed class FailOnceProjectAssetResolver(IProjectAssetResolver source) : IProjectAssetResolver
{
    internal bool FailNextOpen { get; set; }

    /// <inheritdoc />
    public Stream Open(ProjectAsset asset)
    {
        if (FailNextOpen)
        {
            FailNextOpen = false;
            throw new IOException("Simulated project asset read failure.");
        }

        return source.Open(asset);
    }
}
