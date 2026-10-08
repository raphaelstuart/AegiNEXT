using AegiNext.Application.Tasks;

namespace AegiNext.Application.Tests.Tasks;

public sealed class AegiTaskResourceTests
{
    [Fact]
    public void StorageIdentityResolvesRelativeSegmentsAndExistingDirectoryLinks()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aegi-task-resource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var actual = Path.Combine(directory, "actual");
            Directory.CreateDirectory(actual);
            var link = Path.Combine(directory, "alias");
            Directory.CreateSymbolicLink(link, actual);
            var actualResource = AegiTaskResource.StoragePath(Path.Combine(actual, "pending", "target.json"));
            var linkResource = AegiTaskResource.StoragePath(Path.Combine(link, "pending", "target.json"));
            Assert.Equal(actualResource, linkResource);
            Assert.Equal(AegiTaskResource.StoragePath(Path.Combine(actual, "target.json")),
                AegiTaskResource.StoragePath(Path.Combine(actual, "child", "..", "target.json")));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void StorageIdentityPreservesDistinctCaseSensitiveTargets()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aegi-task-resource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "mixedCase.json");
            File.WriteAllText(path, "value");
            var alternative = Path.Combine(directory, "MIXEDCASE.JSON");
            if (File.Exists(alternative))
            {
                Assert.Equal(AegiTaskResource.StoragePath(path), AegiTaskResource.StoragePath(alternative));
            }
            else
            {
                Assert.NotEqual(AegiTaskResource.StoragePath(path), AegiTaskResource.StoragePath(alternative));
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void StorageIdentityRejectsSymbolicLinkCycles()
    {
        var directory = Path.Combine(Path.GetTempPath(), "aegi-task-resource-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var first = Path.Combine(directory, "first.json");
            var second = Path.Combine(directory, "second.json");
            File.CreateSymbolicLink(first, second);
            File.CreateSymbolicLink(second, first);

            Assert.ThrowsAny<IOException>(() => AegiTaskResource.StoragePath(first));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
