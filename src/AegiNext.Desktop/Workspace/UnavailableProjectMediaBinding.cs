using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal sealed record UnavailableProjectMediaBinding(ProjectMediaBinding Binding, string MediaPath)
{
    internal bool Matches(ProjectDocument document, string directory)
    {
        if (document.Media != Binding)
        {
            return false;
        }

        var asset = document.Assets.Single(asset => asset.Id == Binding.AssetId);
        var path = asset.ExternalPath is { } external && !Path.IsPathFullyQualified(external)
            ? external : ProjectAssetLocation.Resolve(asset, directory);
        return string.Equals(MediaPath, path, OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
