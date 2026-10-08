using System.Security.Cryptography;
using AegiNext.Core.Projects;

namespace AegiNext.Application.Tests;

public sealed class ProjectStoreFingerprintTests
{
    [Fact]
    public async Task StreamedFingerprintMatchesCanonicalBytesAndSavedFile()
    {
        using var directory = new TemporaryProjectDirectory();
        var editor = new ProjectEditor();
        var id = editor.AddSubtitle(new(1001, 30000), new(2002, 30000), "字幕 😀\n\"مرحبا\" \\ a\u0301");
        editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(0), 0.25));
        editor.UpdateSubtitle(id, line => line with
        {
            InactiveKaraoke = [new(0, 1, new(0), new(1, 30000), SceneColor.White)]
        });
        var document = editor.Snapshot;
        var expected = Convert.ToHexStringLower(SHA256.HashData(ProjectStore.Serialize(document)));

        Assert.Equal(expected, ProjectStore.ComputeFingerprint(document));
        ProjectStore.ValidateSerialization(document);
        var path = Path.Combine(directory.Path, "project.aeginext");
        await ProjectStore.SaveAsync(document, path);
        await using var stream = File.OpenRead(path);
        Assert.Equal(expected, Convert.ToHexStringLower(await SHA256.HashDataAsync(stream)));
    }

    [Fact]
    public void FingerprintAndSerializationValidationRejectInvalidSnapshots()
    {
        var document = new ProjectDocument
        {
            Width = 0
        };

        Assert.Throws<InvalidDataException>(() => ProjectStore.ComputeFingerprint(document));
        Assert.Throws<InvalidDataException>(() => ProjectStore.ValidateSerialization(document));
    }
}
