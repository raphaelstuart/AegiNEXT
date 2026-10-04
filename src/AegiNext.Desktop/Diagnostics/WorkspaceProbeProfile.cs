namespace AegiNext.Desktop.Diagnostics;

internal static class WorkspaceProbeProfile
{
    private static readonly string[] sourceFiles = ["preferences.json", "subtitle-styles.aegistyles"];

    internal static void Prepare(WorkspaceProbeOptions options)
    {
        if (Directory.Exists(options.ProfileDirectory))
        {
            throw new InvalidOperationException("诊断目录必须是尚未创建的独立目录。");
        }
        if (options.SourceProfileDirectory is { } sourceDirectory && !Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException("诊断来源目录不存在。");
        }

        Directory.CreateDirectory(options.ProfileDirectory);
        if (options.SourceProfileDirectory is not { } source)
        {
            return;
        }
        foreach (var name in sourceFiles)
        {
            var path = Path.Combine(source, name);
            if (File.Exists(path))
            {
                File.Copy(path, Path.Combine(options.ProfileDirectory, name));
            }
        }
    }
}
