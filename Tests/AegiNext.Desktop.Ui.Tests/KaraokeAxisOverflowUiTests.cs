using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using Avalonia;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KaraokeAxisOverflowUiTests
{
    [AvaloniaFact]
    public void OverflowRemainsEditableWithoutMovingNeighborsAndEachReleaseCanUndoOnce()
    {
        var line = new SubtitleLine
        {
            Text = "ab", End = new(4), Karaoke =
            [new(0, 1, MediaTime.Zero, new(9), SceneColor.White), new(1, 1, new(9), new(10), SceneColor.White)]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers =
            [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }] };
        var editor = new ProjectEditor(document);
        using var host = new KaraokeAxisUiTestHost(line);
        host.Axis.RangeRequested += (_, e) =>
        {
            editor.SetKaraokeClipRange(e.SubtitleId, e.ClipId, e.Start, e.End);
            host.Replace(editor.Snapshot.Subtitles[0], e.ClipId);
        };
        Assert.True(host.Axis.HasOverflow);
        var pixels = host.Axis.Viewport.PixelsPerSecond;
        var first = line.Karaoke[0];
        host.Drag(host.Axis.GeometryFor(first.Id).EndHandle.Center, new(-6 * pixels, 0));
        Assert.Equal(new MediaTime(3), editor.Snapshot.Subtitles[0].Karaoke[0].End);
        Assert.Equal(line.Karaoke[1], editor.Snapshot.Subtitles[0].Karaoke[1]);
        Assert.True(host.Axis.HasOverflow);
        pixels = host.Axis.Viewport.PixelsPerSecond;
        host.Drag(host.Axis.GeometryFor(line.Karaoke[1].Id).Body.Center, new(-8 * pixels, 0));
        Assert.Equal(new MediaTime(1), editor.Snapshot.Subtitles[0].Karaoke[1].Start);
        Assert.Equal(new MediaTime(2), editor.Snapshot.Subtitles[0].Karaoke[1].End);
        Assert.False(host.Axis.HasOverflow);
        Assert.Equal(line.End, editor.Snapshot.Subtitles[0].End);
        Assert.True(editor.Undo());
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }
}
