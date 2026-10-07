namespace AegiNext.Desktop.Settings.Transfer;

internal static class SettingsRestoreStartup
{
    private static readonly Lock gate = new();
    private static readonly HashSet<string> initializedDirectories = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static readonly Dictionary<string, Exception> errors = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    internal static void ApplyOnce(string directory)
    {
        var fullPath = UserSettingsTransferFiles.ResolvePhysicalPath(directory);
        lock (gate)
        {
            if (initializedDirectories.Contains(fullPath))
            {
                return;
            }

            using var restore = new UserSettingsRestoreService(fullPath);
            try
            {
                restore.ApplyPending();
            }
            catch (Exception error) when (!restore.HasRecoveryJournal)
            {
                errors[fullPath] = error;
            }
            initializedDirectories.Add(fullPath);
        }
    }

    internal static Exception? GetError(string directory)
    {
        var fullPath = UserSettingsTransferFiles.ResolvePhysicalPath(directory);
        lock (gate)
        {
            return errors.GetValueOrDefault(fullPath);
        }
    }
}
