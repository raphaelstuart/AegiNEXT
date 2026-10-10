using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KaraokeAxisRangeUiTests
{
    [AvaloniaTheory]
    [InlineData(0, "start", 0.35)]
    [InlineData(0, "start", -0.35)]
    [InlineData(1, "start", -0.35)]
    [InlineData(0, "end", 0.35)]
    [InlineData(1, "end", -0.35)]
    [InlineData(0, "move", 0.35)]
    [InlineData(0, "move", -0.35)]
    [InlineData(1, "move", -0.35)]
    public void EveryGroupEditsOnlyItsOwnRequestedRange(int index, string gesture, double seconds)
    {
        var line = Line();
        using var host = new KaraokeAxisUiTestHost(line, new(1, 2));
        host.Axis.SetContent(line, host.Offset, line.Karaoke[index].Id);
        var clip = line.Karaoke[index];
        var before = line.Karaoke.Select(value => host.Axis.GeometryFor(value.Id)).ToArray();
        var geometry = before[index];
        var start = gesture switch { "start" => geometry.StartHandle.Center, "end" => geometry.EndHandle.Center, _ => geometry.Body.Center };
        var point = host.Point(start);
        var pixels = host.Axis.Viewport.PixelsPerSecond;
        var delta = Time(seconds);
        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseMove(point + new Vector(seconds * pixels, 0));
        Assert.Empty(host.Requests);
        Assert.Equal(before[1 - index], host.Axis.GeometryFor(line.Karaoke[1 - index].Id));
        Assert.Equal(line.Karaoke[index], clip);
        host.Window.MouseUp(point + new Vector(seconds * pixels, 0), MouseButton.Left);
        var request = Assert.Single(host.Requests);
        Assert.Same(line, request.BaselineLine);
        Assert.Equal(host.Offset, request.AnimationOffset);
        Assert.Equal(line.Id, request.SubtitleId);
        Assert.Equal(clip.Id, request.ClipId);
        Assert.Equal(clip.Start + (gesture != "end" ? delta : MediaTime.Zero), request.Start);
        Assert.Equal(clip.End + (gesture != "start" ? delta : MediaTime.Zero), request.End);
        Assert.Empty(host.EditRequests);
        Assert.Equal(before[index], host.Axis.GeometryFor(clip.Id));
    }

    [AvaloniaTheory]
    [InlineData("start")]
    [InlineData("end")]
    [InlineData("move")]
    public void ClickAndSubThresholdJitterOpenThePopupWithoutQuantizingTheRange(string gesture)
    {
        using var host = new KaraokeAxisUiTestHost(Line());
        host.Axis.IsSnapEnabled = true;
        var clip = host.Line.Karaoke[0];
        var geometry = host.Axis.GeometryFor(clip.Id);
        var point = gesture switch { "start" => geometry.StartHandle.Center, "end" => geometry.EndHandle.Center, _ => geometry.Body.Center };
        host.Drag(point, new(2, 0));
        Assert.Empty(host.Requests);
        var request = Assert.Single(host.EditRequests);
        Assert.Equal(clip.Id, request.ClipId);
        Assert.Equal(geometry.Body, request.Anchor);
    }

    [AvaloniaTheory]
    [InlineData("escape")]
    [InlineData("captureloss")]
    [InlineData("stale")]
    [InlineData("offset")]
    [InlineData("selection")]
    [InlineData("detach")]
    [InlineData("hidden")]
    [InlineData("disable")]
    [InlineData("cancel")]
    public void InterruptedGestureNeverSubmitsOrOpensThePopup(string cancellation)
    {
        using var host = new KaraokeAxisUiTestHost(Line());
        var point = host.Point(host.Axis.GeometryFor(host.Line.Karaoke[0].Id).EndHandle.Center);
        host.Window.MouseDown(point, MouseButton.Left);
        Assert.Same(host.Axis, host.Pointer!.Captured);
        host.Window.MouseMove(point + new Vector(40, 0));
        Assert.True(host.Axis.HasActiveGesture);
        switch (cancellation)
        {
            case "escape": UiTestActions.Press(host.Window, Key.Escape); break;
            case "captureloss": host.Pointer.Capture(null); break;
            case "stale": host.Axis.SetContent(host.Line with { Text = "cd" }, host.Offset, host.Line.Karaoke[0].Id); break;
            case "offset": host.Axis.SetContent(host.Line, new(1, 2), host.Line.Karaoke[0].Id); break;
            case "selection": host.Axis.SetContent(host.Line, host.Offset, host.Line.Karaoke[1].Id); break;
            case "detach": host.Window.Content = null; break;
            case "hidden": host.Axis.IsVisible = false; break;
            case "disable": host.Axis.IsEnabled = false; break;
            default: host.Axis.CancelGesture(); break;
        }
        Assert.Null(host.Pointer.Captured);
        Assert.False(host.Axis.HasActiveGesture);
        host.Window.MouseUp(point + new Vector(40, 0), MouseButton.Left);
        Assert.Empty(host.Requests);
        Assert.Empty(host.EditRequests);
    }

    [AvaloniaFact]
    public void SourceChangeDuringSelectionDoesNotStartAStaleGesture()
    {
        using var host = new KaraokeAxisUiTestHost(Line());
        host.Axis.ClipSelectionRequested += (_, e) => host.Axis.SetContent(host.Line with { End = new(6) }, host.Offset, e.ClipId);
        var point = host.Axis.GeometryFor(host.Line.Karaoke[1].Id).Body.Center;
        host.Drag(point, new(30, 0));
        Assert.Empty(host.Requests);
        Assert.Empty(host.EditRequests);
        Assert.False(host.Axis.HasActiveGesture);
    }

    [AvaloniaFact]
    public void ViewportAndFitAreLocalAndFrozenDuringRangeGestureThenOneReleaseCanCreateOneUndo()
    {
        var line = Line();
        var document = new ProjectDocument { Subtitles = [line], Layers =
            [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }] };
        var editor = new ProjectEditor(document);
        using var host = new KaraokeAxisUiTestHost(line);
        host.Axis.RangeRequested += (_, e) => editor.SetKaraokeClipRange(e.SubtitleId, e.ClipId, e.Start, e.End);
        var point = host.Point(host.Axis.GeometryFor(line.Karaoke[0].Id).Body.Center);
        var fitted = host.Axis.Viewport;
        host.Window.MouseWheel(point, new(0, 4), RawInputModifiers.Control);
        Assert.True(host.Axis.Viewport.PixelsPerSecond > fitted.PixelsPerSecond);
        host.Window.MouseWheel(point, new(-1, -1));
        host.Window.MouseWheel(point, new(0, -1), RawInputModifiers.Shift);
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
        Assert.Empty(host.Requests);
        host.Axis.FitToContent();
        Assert.Equal(fitted, host.Axis.Viewport);
        host.Window.MouseDown(point, MouseButton.Left);
        var frozen = host.Axis.Viewport;
        host.Window.MouseWheel(point, new(0, 4), RawInputModifiers.Control);
        host.Window.MouseWheel(point, new(-1, -1));
        host.Axis.FitToContent();
        Assert.Equal(frozen, host.Axis.Viewport);
        host.Window.MouseMove(point + new Vector(20, 0));
        Assert.False(editor.CanUndo);
        host.Window.MouseUp(point + new Vector(20, 0), MouseButton.Left);
        Assert.Single(host.Requests);
        Assert.True(editor.CanUndo);
        Assert.True(editor.Undo());
        Assert.Same(document, editor.Snapshot);
        Assert.False(editor.CanUndo);
    }

    [AvaloniaFact]
    public void ExtremelyShortRationalGroupHasSeparatedHandlesAndPreservesExactDurationOnMove()
    {
        var start = new MediaTime(1, 3);
        var duration = new MediaTime(1, 1000000000);
        var clip = new KaraokeSegment(0, 1, start, start + duration, SceneColor.White);
        var line = new SubtitleLine { Text = "a", End = new(4), Karaoke = [clip] };
        using var host = new KaraokeAxisUiTestHost(line);
        var geometry = host.Axis.GeometryFor(clip.Id);
        Assert.True(geometry.TimeBounds.Width < 0.001);
        Assert.Equal(1, geometry.Body.Width);
        Assert.True(geometry.StartHandle.Right < geometry.EndHandle.Left);
        host.Drag(geometry.Body.Center, new(20, 0));
        var request = Assert.Single(host.Requests);
        Assert.Equal(duration, request.End - request.Start);
        Assert.Equal(start, clip.Start);
        host.Drag(geometry.StartHandle.Center, new(20, 0));
        Assert.Single(host.Requests);
        host.Drag(geometry.EndHandle.Center, new(-20, 0));
        Assert.Single(host.Requests);
    }

    [AvaloniaFact]
    public void FloatingHandlesAtContentZeroRemainInsideTheTrackAndCanBeDragged()
    {
        var line = new SubtitleLine { Text = "a", End = new(4), Karaoke = [new(0, 1, MediaTime.Zero, new(1, 1000000), SceneColor.White)] };
        using var host = new KaraokeAxisUiTestHost(line);
        var geometry = host.Axis.GeometryFor(line.Karaoke[0].Id);
        Assert.True(geometry.StartHandle.Left >= 12);
        Assert.True(geometry.EndHandle.Left > geometry.StartHandle.Right);
        host.Drag(geometry.EndHandle.Center, new(20, 0));
        Assert.True(Assert.Single(host.Requests).End > line.Karaoke[0].End);
    }

    [AvaloniaFact]
    public void ResizeDuringDragKeepsTheTimeProjectionFrozen()
    {
        using var host = new KaraokeAxisUiTestHost(Line());
        var clip = host.Line.Karaoke[0];
        var point = host.Point(host.Axis.GeometryFor(clip.Id).EndHandle.Center);
        var viewport = host.Axis.Viewport;
        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.Width = 620;
        host.Flush();
        Assert.Equal(viewport, host.Axis.Viewport);
        host.Window.MouseMove(point + new Vector(20, 0));
        host.Window.MouseUp(point + new Vector(20, 0), MouseButton.Left);
        var request = Assert.Single(host.Requests);
        Assert.Equal(clip.End + new MediaTime((long)Math.Round(20 / viewport.PixelsPerSecond * TimeSpan.TicksPerSecond), TimeSpan.TicksPerSecond), request.End);
    }

    [AvaloniaFact]
    public void IdleAxisLeavesEscapeAvailableToTheWindowCommandRouter()
    {
        using var host = new KaraokeAxisUiTestHost(Line());
        var routed = false;
        host.Window.AddHandler(InputElement.KeyDownEvent, (_, e) => routed |= e.Key == Key.Escape, RoutingStrategies.Bubble);
        Assert.True(host.Axis.Focus());
        UiTestActions.Press(host.Window, Key.Escape);
        Assert.True(routed);
    }

    private static SubtitleLine Line() => new()
    {
        Text = "ab", End = new(4), Karaoke =
        [new(0, 1, new(1, 2), new(3, 2), SceneColor.White), new(1, 1, new(2), new(3), SceneColor.White)]
    };
    private static MediaTime Time(double value) => new((long)Math.Round(value * 1000000), 1000000);
}
