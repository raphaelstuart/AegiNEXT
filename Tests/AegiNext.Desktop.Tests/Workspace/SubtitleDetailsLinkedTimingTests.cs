using AegiNext.Core.Projects;
using AegiNext.Core.Timing;

namespace AegiNext.Desktop.Tests.Workspace;

[Collection("Workspace session")]
public sealed class SubtitleDetailsLinkedTimingTests
{
    [Theory]
    [InlineData("Start")]
    [InlineData("End")]
    [InlineData("Duration")]
    public async Task LinkedInputsMoveTheirSideOfTheTextAndCommitOneUndo(string field)
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        Assert.True(details.SelectClip(line.Karaoke[1].Id));
        details.LinkedTimingEnabled = true;
        switch (field)
        {
            case "Start":
                details.EditStart("3");
                break;
            case "End":
                details.EditEnd("4");
                break;
            default:
                details.EditDuration("2");
                break;
        }
        Assert.True(details.TryCommit(), details.Error);
        var changed = context.Editor.Snapshot.Subtitles[0];
        var previousDelta = field == "Start" ? new MediaTime(1, 2) : MediaTime.Zero;
        var nextDelta = field switch { "End" => new MediaTime(1, 2), "Duration" => new MediaTime(1), _ => MediaTime.Zero };
        Assert.Equal(line.Karaoke[0].Start + previousDelta, changed.Karaoke[0].Start);
        Assert.Equal(line.Karaoke[0].End + previousDelta, changed.Karaoke[0].End);
        Assert.Equal(line.Karaoke[2].Start + nextDelta, changed.Karaoke[2].Start);
        Assert.Equal(line.Karaoke[2].End + nextDelta, changed.Karaoke[2].End);
        Assert.Equal(line.Karaoke.Select(clip => clip.Id), changed.Karaoke.Select(clip => clip.Id));
        Assert.Equal(original.Layers, context.Editor.Snapshot.Layers);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task BothEndpointDraftsLinkBothSidesInOneTransaction()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        var line = original.Subtitles[0];
        context.Session.SelectCue(line.Id);
        var details = context.Session.Details;
        details.SelectClip(line.Karaoke[1].Id);
        details.LinkedTimingEnabled = true;
        details.EditStart("3");
        details.EditEnd("4");
        Assert.True(details.TryCommit(), details.Error);
        var changed = context.Editor.Snapshot.Subtitles[0];
        Assert.Equal(new MediaTime(3, 2), changed.Karaoke[0].Start);
        Assert.Equal(new MediaTime(5, 2), changed.Karaoke[0].End);
        Assert.Equal(new MediaTime(3), changed.Karaoke[1].Start);
        Assert.Equal(new MediaTime(4), changed.Karaoke[1].End);
        Assert.Equal(new MediaTime(9, 2), changed.Karaoke[2].Start);
        Assert.Equal(new MediaTime(11, 2), changed.Karaoke[2].End);
        Assert.True(context.Editor.Undo());
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
    }

    [Fact]
    public async Task LinkedInputsRejectNegativePeerTimeWithoutChangingTheProject()
    {
        await using var context = new WorkspaceSessionTestContext(Document());
        await context.InitializeAsync();
        var original = context.Editor.Snapshot;
        context.Session.SelectCue(original.Subtitles[0].Id);
        var details = context.Session.Details;
        details.SelectClip(original.Subtitles[0].Karaoke[1].Id);
        details.LinkedTimingEnabled = true;
        details.EditStart("1");
        Assert.False(details.TryCommit());
        Assert.Equal("Start", details.InvalidFieldKey);
        Assert.Equal("1", details.StartText);
        Assert.Same(original, context.Editor.Snapshot);
        Assert.False(context.Editor.CanUndo);
        details.Restore("Timing");
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine
        {
            Text = "abc", End = new(6),
            Karaoke = [new(0, 1, new(1), new(2), SceneColor.White),
                new(1, 1, new(5, 2), new(7, 2), SceneColor.White),
                new(2, 1, new(4), new(5), SceneColor.White)]
        };
        return new() { Subtitles = [line], Layers = [new()
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
        }] };
    }
}
