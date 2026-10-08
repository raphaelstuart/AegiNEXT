using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Workspace;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class VideoFileTypesTests
{
    [Theory]
    [InlineData(".mkv")]
    [InlineData(".mp4")]
    [InlineData(".mov")]
    [InlineData(".webm")]
    [InlineData(".avi")]
    [InlineData(".m4v")]
    [InlineData(".ts")]
    [InlineData(".m2ts")]
    [InlineData(".mts")]
    [InlineData(".mpg")]
    [InlineData(".mpeg")]
    [InlineData(".m2v")]
    [InlineData(".vob")]
    [InlineData(".wmv")]
    [InlineData(".flv")]
    public void PickerAndDroppedPathsAcceptCommonContainersRegardlessOfCase(string extension)
    {
        Assert.Contains("*" + extension, VideoFileTypes.Patterns);
        Assert.True(VideoFileTypes.SupportsPath(Path.Combine("字幕素材", "episode 01" + extension)));
        Assert.True(VideoFileTypes.SupportsPath(Path.Combine("字幕素材", "episode 01" + extension.ToUpperInvariant())));
    }

    [Theory]
    [InlineData("video")]
    [InlineData("video.srt")]
    [InlineData("video.mp3")]
    [InlineData("video.mkv.tmp")]
    [InlineData("directory.mkv/video.txt")]
    public void UnsupportedDroppedExtensionsRemainExcluded(string path)
    {
        Assert.False(VideoFileTypes.SupportsPath(path));
    }

    [Fact]
    public async Task OpenMediaPickerUsesTheSamePatternsAsDroppedPaths()
    {
        await using var context = new ProjectMediaPathTestContext();
        await context.Session.Styles.Completion;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.OPEN_MEDIA);

        Assert.Null(context.Session.LastError);
        var patterns = Assert.IsType<string[]>(context.Dialogs.OpenFilePatterns);
        Assert.Equal(VideoFileTypes.Patterns, patterns);
        Assert.Equal(patterns.Length, patterns.Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(patterns, pattern => Assert.True(VideoFileTypes.SupportsPath("dropped" + pattern[1..])));
        Assert.Empty(context.ProbePaths);
    }
}
