using AegiNext.Core.Timing;
using AegiNext.Media.Analysis;

namespace AegiNext.Media.Tests.Analysis;

public sealed class AudioAnalysisCacheIdentityTests
{
    [Fact]
    public void MovingAMediaFilePreservesIdentityButChangingSampledContentInvalidatesIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AegiNext-cache-test", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "audio.bin");
            File.WriteAllBytes(path, Enumerable.Range(0, 256000).Select(index => (byte)index).ToArray());
            var modified = File.GetLastWriteTimeUtc(path);
            var original = AudioAnalysisCacheIdentity.Create(path, 0, new(MediaTime.Zero), new(1));
            var moved = Path.Combine(directory, "renamed.bin");
            File.Move(path, moved);
            Assert.Equal(original, AudioAnalysisCacheIdentity.Create(moved, 0, new(MediaTime.Zero), new(1)));
            using (var file = File.OpenWrite(moved))
            {
                file.Position = 128000;
                file.WriteByte(0xFF);
            }
            File.SetLastWriteTimeUtc(moved, modified);
            Assert.NotEqual(original, AudioAnalysisCacheIdentity.Create(moved, 0, new(MediaTime.Zero), new(1)));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void IdentityIncludesExactTimelineRationalsAndSelectedStream()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            File.WriteAllBytes(path, [1, 2, 3]);
            var original = AudioAnalysisCacheIdentity.Create(path, 0, new(MediaTime.Zero), new(1));
            Assert.NotEqual(original, AudioAnalysisCacheIdentity.Create(path, 1, new(MediaTime.Zero), new(1)));
            Assert.NotEqual(original, AudioAnalysisCacheIdentity.Create(path, 0, new(new(1, 480001)), new(1)));
            Assert.NotEqual(original, AudioAnalysisCacheIdentity.Create(path, 0, new(MediaTime.Zero), new(480002, 480001)));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
