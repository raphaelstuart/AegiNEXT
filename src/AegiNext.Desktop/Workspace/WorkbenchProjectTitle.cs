using AegiNext.Core.Projects;

namespace AegiNext.Desktop.Workspace;

internal static class WorkbenchProjectTitle
{
    internal static string GetDisplayName(ProjectDocument document, string? projectPath, string untitledName)
    {
        if (projectPath is not null)
        {
            return Path.GetFileNameWithoutExtension(projectPath);
        }

        if (!string.Equals(document.Name, "Untitled", StringComparison.Ordinal))
        {
            return document.Name;
        }

        if (document.Media is { } media)
        {
            var asset = document.Assets.Single(value => value.Id == media.AssetId);
            var name = Path.GetFileNameWithoutExtension(asset.ExternalPath ?? asset.RelativePath);
            if (!string.IsNullOrWhiteSpace(name))
            {
                return name;
            }
        }

        return untitledName;
    }

    internal static string Format(string displayName, bool isDirty)
    {
        return $"AegiNEXT - {displayName}{(isDirty ? " •" : string.Empty)}";
    }
}
