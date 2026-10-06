namespace AegiNext.Desktop.Tests;

internal sealed class TemporaryWorkbenchDirectory : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "AegiNext-workbench-tests-" + Guid.NewGuid().ToString("N"));

    internal TemporaryWorkbenchDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        Directory.Delete(Path, true);
    }
}
