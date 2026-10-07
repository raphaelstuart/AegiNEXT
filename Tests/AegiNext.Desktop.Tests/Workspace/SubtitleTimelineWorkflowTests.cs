using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleTimelineWorkflowTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task KnownPlaybackOriginImportsExternalZeroAsNegativeProjectTimeAndExportsItBack(bool ass)
    {
        await using var context = new WorkspaceSessionTestContext(Document(new(1, 10), MediaTime.Zero));
        await context.InitializeAsync();
        context.Dialogs.ConversionChoice = true;
        var source = Path.Combine(context.DirectoryPath, ass ? "source.ass" : "source.srt");
        var output = Path.Combine(context.DirectoryPath, ass ? "output.ass" : "output.srt");
        await File.WriteAllTextAsync(source, ass
            ? "[Script Info]\nPlayResX: 1920\nPlayResY: 1080\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:00.50,Default,,0,0,0,,{\\kf20}a\n"
            : "1\n00:00:00,000 --> 00:00:00,500\na\n");
        context.Dialogs.OpenPath = source;

        await context.Session.ExecuteCommandAsync(ass ? WorkbenchCommand.IMPORT_ASS : WorkbenchCommand.IMPORT_SUBTITLES);

        Assert.Null(context.Session.LastError);
        var line = Assert.Single(context.Editor.Snapshot.Subtitles);
        var layer = Assert.Single(context.Editor.Snapshot.Layers);
        Assert.Equal(new MediaTime(-1, 10), line.Start);
        Assert.Equal(new MediaTime(2, 5), line.End);
        Assert.Equal((line.Start, line.End), (layer.Start, layer.End));
        if (ass)
        {
            Assert.Equal(MediaTime.Zero, Assert.Single(line.Karaoke).Start);
            Assert.Equal(new MediaTime(1, 5), line.Karaoke[0].End);
        }
        context.Dialogs.SavePath = output;
        await context.Session.ExecuteCommandAsync(ass ? WorkbenchCommand.EXPORT_ASS : WorkbenchCommand.EXPORT_SUBTITLES);
        Assert.Null(context.Session.LastError);
        var text = await File.ReadAllTextAsync(output);
        Assert.Contains(ass ? "0:00:00.00,0:00:00.50" : "00:00:00,000 --> 00:00:00,500", text, StringComparison.Ordinal);
        Assert.Equal(new MediaTime(-1, 10), context.Editor.Snapshot.Subtitles[0].Start);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OfflineUnknownOriginRequiresConversionReviewAndNeverInventsConfirmedMetadata(bool export)
    {
        var document = Document(new(3), null);
        if (export)
        {
            var line = new SubtitleLine { Start = MediaTime.Zero, End = new(1), Text = "cue" };
            document = document with
            {
                Subtitles = [line],
                Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
            };
        }
        await using var context = new WorkspaceSessionTestContext(document);
        await context.InitializeAsync();
        var path = Path.Combine(context.DirectoryPath, "subtitle.srt");
        var command = export ? WorkbenchCommand.EXPORT_SUBTITLES : WorkbenchCommand.IMPORT_SUBTITLES;
        if (export)
        {
            context.Dialogs.SavePath = path;
        }
        else
        {
            await File.WriteAllTextAsync(path, "1\n00:00:00,000 --> 00:00:01,000\ncue\n");
            context.Dialogs.OpenPath = path;
        }
        var before = context.Editor.Snapshot;
        Assert.True(context.Session.CanExecuteCommand(command));

        await context.Session.ExecuteCommandAsync(command);

        Assert.Null(context.Session.LastError);
        Assert.Contains(context.Dialogs.ConversionDiagnostics, message => message.Contains("Subtitle.PlaybackOriginUnknown", StringComparison.Ordinal));
        Assert.Same(before, context.Editor.Snapshot);
        if (export)
        {
            Assert.False(File.Exists(path));
        }
        context.Dialogs.ConversionChoice = true;
        await context.Session.ExecuteCommandAsync(command);
        Assert.Null(context.Session.LastError);
        Assert.Null(context.Editor.Snapshot.Media!.PlaybackOrigin);
        Assert.Equal(MediaTime.Zero, Assert.Single(context.Editor.Snapshot.Subtitles).Start);
        if (export)
        {
            Assert.True(File.Exists(path));
        }
    }

    private static ProjectDocument Document(MediaTime mediaOrigin, MediaTime? playbackOrigin)
    {
        var asset = new ProjectAsset(Guid.NewGuid(), ProjectAssetKind.MEDIA, string.Empty, ExternalPath: "/unavailable/source.mkv");
        return new()
        {
            Assets = [asset], Media = new(asset.Id, 0, null, mediaOrigin) { PlaybackOrigin = playbackOrigin }
        };
    }
}
