using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KaraokeAxisLinkedTimingUiTests
{
    [AvaloniaTheory]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("move")]
    public void ReverseTimedPeersClampAtTheContentOriginAndPreviewWithFixedLanes(string gesture)
    {
        var line = new SubtitleLine
        {
            Text = "abc", End = new(6), Karaoke =
            [new(0, 1, new(1, 4), new(5, 4), SceneColor.White),
                new(1, 1, new(2), new(5), SceneColor.White),
                new(2, 1, new(1, 4), new(3, 4), SceneColor.White)]
        };
        using var host = new KaraokeAxisUiTestHost(line);
        host.Axis.IsTimingLinked = true;
        host.Axis.KeepDurationLabelsVisible = true;
        host.Axis.SetContent(line, MediaTime.Zero, line.Karaoke[1].Id);
        var before = line.Karaoke.Select(clip => host.Axis.GeometryFor(clip.Id)).ToArray();
        var lanes = host.Axis.ClipLanes.ToDictionary();
        var geometry = before[1];
        var point = host.Point(gesture switch
        {
            "start" => geometry.StartHandle.Center,
            "end" => geometry.EndHandle.Center,
            _ => geometry.Body.Center
        });
        var pixels = host.Axis.Viewport.PixelsPerSecond;
        var movement = new Vector(-pixels, 0);
        var viewport = host.Axis.Viewport;
        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseMove(point + movement);
        Assert.Empty(host.Requests);
        Assert.Equal(viewport, host.Axis.Viewport);
        Assert.All(line.Karaoke, clip => Assert.Equal(lanes[clip.Id], host.Axis.ClipLanes[clip.Id]));
        var prefixMoves = gesture != "end";
        var suffixMoves = gesture != "start";
        Assert.Equal(before[0].Body.X - (prefixMoves ? pixels / 4 : 0), host.Axis.GeometryFor(line.Karaoke[0].Id).Body.X, 6);
        Assert.Equal(before[2].Body.X - (suffixMoves ? pixels / 4 : 0), host.Axis.GeometryFor(line.Karaoke[2].Id).Body.X, 6);
        foreach (var clip in line.Karaoke)
        {
            var label = Assert.Single(host.Axis.DurationLabels, value => value.ClipId == clip.Id);
            Assert.Equal(host.Axis.GeometryFor(clip.Id).Body.Center.X, label.Bounds.Center.X, 6);
            var expected = clip.End - clip.Start;
            if (clip.Id == line.Karaoke[1].Id)
            {
                expected += gesture switch
                {
                    "start" => new MediaTime(1, 4), "end" => new(-1, 4), _ => MediaTime.Zero
                };
            }
            Assert.Equal(expected, label.Duration);
        }
        host.Window.MouseUp(point + movement, MouseButton.Left);
        var request = Assert.Single(host.Requests);
        Assert.True(request.IsTimingLinked);
        Assert.Equal(line.Karaoke[1].Start - (prefixMoves ? new MediaTime(1, 4) : MediaTime.Zero), request.Start);
        Assert.Equal(line.Karaoke[1].End - (suffixMoves ? new MediaTime(1, 4) : MediaTime.Zero), request.End);
    }

    [AvaloniaTheory]
    [InlineData("start", 3.1)]
    [InlineData("end", 4.1)]
    [InlineData("move", 3.1)]
    public void SnapDoesNotAttractTheTargetToStaleEdgesOfMovingPeers(string gesture, double edge)
    {
        var line = new SubtitleLine
        {
            Text = "abc", End = new(6), Karaoke =
            [new(0, 1, new(1), new(16, 5), SceneColor.White),
                new(1, 1, new(3), new(4), SceneColor.White),
                new(2, 1, new(21, 5), new(5), SceneColor.White)]
        };
        using var host = new KaraokeAxisUiTestHost(line);
        host.Axis.IsTimingLinked = true;
        host.Axis.IsSnapEnabled = true;
        host.Axis.SetContent(line, MediaTime.Zero, line.Karaoke[1].Id);
        var geometry = host.Axis.GeometryFor(line.Karaoke[1].Id);
        host.Drag(gesture switch
        {
            "start" => geometry.StartHandle.Center,
            "end" => geometry.EndHandle.Center,
            _ => geometry.Body.Center
        }, new(host.Axis.Viewport.PixelsPerSecond / 10, 0));
        var request = Assert.Single(host.Requests);
        Assert.Equal(new MediaTime((long)Math.Round(edge * 10), 10), gesture == "end" ? request.End : request.Start);
    }

    [AvaloniaFact]
    public void ReturningTheLinkedBodyToItsStartRestoresEveryPreviewAndDoesNotCreateUndo()
    {
        var line = new SubtitleLine
        {
            Text = "abc", End = new(6), Karaoke =
            [new(0, 1, new(1), new(2), SceneColor.White),
                new(1, 1, new(5, 2), new(7, 2), SceneColor.White),
                new(2, 1, new(4), new(5), SceneColor.White)]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers =
            [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }] };
        var editor = new ProjectEditor(document);
        using var host = new KaraokeAxisUiTestHost(line);
        host.Axis.IsTimingLinked = true;
        host.Axis.SetContent(line, MediaTime.Zero, line.Karaoke[1].Id);
        host.Axis.RangeRequested += (_, e) => editor.SetKaraokeClipRange(e.SubtitleId, e.ClipId, e.Start, e.End, e.IsTimingLinked);
        var before = line.Karaoke.Select(clip => host.Axis.GeometryFor(clip.Id)).ToArray();
        var point = host.Point(before[1].Body.Center);
        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseMove(point + new Vector(30, 0));
        Assert.All(line.Karaoke, clip => Assert.NotEqual(before[clip.Utf16Start], host.Axis.GeometryFor(clip.Id)));
        host.Window.MouseMove(point);
        Assert.All(line.Karaoke, clip => Assert.Equal(before[clip.Utf16Start], host.Axis.GeometryFor(clip.Id)));
        host.Window.MouseUp(point, MouseButton.Left);
        Assert.Empty(host.Requests);
        Assert.Empty(host.EditRequests);
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }
}
