namespace AegiNext.Application.Tests;

internal sealed class TemporaryProjectDirectory : IDisposable
{
    internal string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"aeginext-project-{Guid.NewGuid():N}");

    internal TemporaryProjectDirectory()
    {
        Directory.CreateDirectory(Path);
    }

    public void Dispose()
    {
        Directory.Delete(Path, true);
    }
}
