using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class AssTransformWorkflowTests
{
    [Fact]
    public async Task ImportKeepsTransformAndMoveInTheSelectedClipAndOneUndoRestoresEverything()
    {
        await using var context = new WorkspaceSessionTestContext();
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var path = Path.Combine(context.DirectoryPath, "transform.ass");
        await File.WriteAllTextAsync(path, "[Script Info]\nPlayResX: 1920\nPlayResY: 1080\n[Events]\nFormat: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text\nDialogue: 0,0:00:00.00,0:00:02.00,Default,,0,0,0,,{\\move(200,300,600,500,500,1500)\\fscx150\\fscy200\\frz30}hello\n");
        context.Dialogs.OpenPath = path;
        context.Dialogs.ConversionChoice = true;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.IMPORT_ASS);

        Assert.Null(context.Session.LastError);
        var line = Assert.Single(context.Editor.Snapshot.Subtitles);
        var layer = Assert.Single(context.Editor.Snapshot.Layers);
        Assert.Equal(line.Id, context.Session.SelectedCueId);
        Assert.Equal(layer.Id, context.Session.SelectedLayer!.Id);
        Assert.Equal(new ScenePoint(1.5, 2), layer.Transform.Scale);
        Assert.Equal(-30, layer.Transform.Rotation);
        var position = Assert.IsType<SubtitlePosition>(line.Style.Position);
        Assert.Equal(200, position.Anchor.X * original.Width + position.Offset.X);
        Assert.Equal(300, position.Anchor.Y * original.Height + position.Offset.Y);
        var track = Assert.Single(layer.Tracks, value => value.Property == AnimationProperty.POSITION);
        Assert.Equal(new MediaTime(1, 2), track.Keyframes[0].Time);
        Assert.Equal(new MediaTime(3, 2), track.Keyframes[^1].Time);
        Assert.Equal(default, track.Keyframes[0].Value.Vector);
        Assert.Equal(new ScenePoint(400, 200), track.Keyframes[^1].Value.Vector);
        Assert.DoesNotContain(context.Dialogs.ConversionDiagnostics,
            message => message.Contains("move", StringComparison.OrdinalIgnoreCase));
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task ExportUsesMeasuredPlacementAndLeavesTheSourceAndUndoHistoryUntouched()
    {
        var line = new SubtitleLine
        {
            Start = new(1), End = new(3), Text = "Export transform",
            Style = new()
            {
                FontFamily = "sans-serif", Position = new() { Anchor = new(0.5, 0.5), Pivot = new(0.25, 0.75) }
            }
        };
        var layer = new ProjectLayer
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End,
            Transform = new() { Position = new(30, 40), Scale = new(1.5, 2), Rotation = -30 }
        };
        await using var context = new WorkspaceSessionTestContext(new() { Subtitles = [line], Layers = [layer] });
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Dialogs.SavePath = Path.Combine(context.DirectoryPath, "transform.ass");
        context.Dialogs.ConversionChoice = true;

        await context.Session.ExecuteCommandAsync(WorkbenchCommand.EXPORT_ASS);

        Assert.Null(context.Session.LastError);
        var exported = await File.ReadAllTextAsync(context.Dialogs.SavePath);
        Assert.Contains("\\pos(", exported);
        Assert.Contains("\\fscx150", exported);
        Assert.Contains("\\fscy200", exported);
        Assert.Contains("\\frz30", exported);
        Assert.DoesNotContain(context.Dialogs.ConversionDiagnostics,
            message => message.Contains("Subtitle.Composition", StringComparison.Ordinal));
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }
}
