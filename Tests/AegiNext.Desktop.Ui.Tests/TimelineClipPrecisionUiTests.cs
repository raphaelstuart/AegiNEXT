using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class TimelineClipPrecisionUiTests
{
    [AvaloniaTheory]
    [InlineData("move", 1, 30, 1)]
    [InlineData("move", -1, 30, 1)]
    [InlineData("start", 1, 30, 1)]
    [InlineData("start", -1, 30, 1)]
    [InlineData("end", 1, 30, 1)]
    [InlineData("end", -1, 30, 1)]
    [InlineData("move", 1, 30000, 1001)]
    [InlineData("move", -1, 30000, 1001)]
    [InlineData("start", 1, 30000, 1001)]
    [InlineData("start", -1, 30000, 1001)]
    [InlineData("end", 1, 30000, 1001)]
    [InlineData("end", -1, 30000, 1001)]
    public void DefaultClipGestureMovesOneMillisecondWithoutChangingItsOriginalTimePhase(
        string mode, int direction, long frameNumerator, long frameDenominator)
    {
        var start = new MediaTime(1001, 30000);
        var cue = new SubtitleLine { Start = start, End = start + new MediaTime(1, 5), Text = "Millisecond" };
        var layer = Layer(cue);
        var source = new ProjectDocument
        {
            FrameRate = new(frameNumerator, frameDenominator), Subtitles = [cue], Layers = [layer]
        };
        var editor = new ProjectEditor(source);
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 2000, IsSnapEnabled = false };
        timeline.SetDocument(source, cue.Id, layer);
        var commits = 0;
        timeline.TimingChanged += (_, e) =>
        {
            commits++;
            if (e.IsMove)
            {
                editor.ShiftLayer(e.Id, e.Start - e.OriginalStart);
            }
            else
            {
                editor.SetLayerTiming(e.Id, e.Start, e.End, e.Mode);
            }
        };
        var window = OpenWindow(timeline);
        try
        {
            var original = timeline.GetClipRectangle(layer.Id)!.Value;
            var origin = Origin(original, mode);
            var destination = origin + new Vector(direction * 2, 0);
            window.MouseDown(origin, MouseButton.Left);
            Assert.True(timeline.HasActiveDrag);
            window.MouseMove(destination);
            Assert.Same(source, editor.Snapshot);
            Assert.False(editor.CanUndo);
            var preview = timeline.GetClipRectangle(layer.Id)!.Value;
            Assert.Equal(original.Left + (mode == "end" ? 0 : direction * 2), preview.Left, 6);
            Assert.Equal(original.Right + (mode == "start" ? 0 : direction * 2), preview.Right, 6);
            window.MouseUp(destination, MouseButton.Left);

            Assert.Equal(1, commits);
            var offset = new MediaTime(direction, 1000);
            var result = Assert.Single(editor.Snapshot.Subtitles);
            Assert.Equal(cue.Start + (mode == "end" ? MediaTime.Zero : offset), result.Start);
            Assert.Equal(cue.End + (mode == "start" ? MediaTime.Zero : offset), result.End);
            Assert.True(editor.Undo());
            Assert.Same(source, editor.Snapshot);
            Assert.False(editor.CanUndo);
            Assert.True(editor.Redo());
            Assert.Equal(result, Assert.Single(editor.Snapshot.Subtitles));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 30, 1)]
    [InlineData(true, 30, 1)]
    [InlineData(false, 30000, 1001)]
    [InlineData(true, 30000, 1001)]
    public void EitherEdgeStopsAtOneMillisecondEvenWhenDraggedPastTheOppositeEdge(
        bool rightEdge, long frameNumerator, long frameDenominator)
    {
        var cue = new SubtitleLine { Start = new(1, 100), End = new(21, 100), Text = "Minimum" };
        var layer = Layer(cue);
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 2000, IsSnapEnabled = false };
        timeline.SetDocument(new()
        {
            FrameRate = new(frameNumerator, frameDenominator), Subtitles = [cue], Layers = [layer]
        }, cue.Id, layer);
        TimelineTimingEventArgs? committed = null;
        timeline.TimingChanged += (_, e) => committed = e;
        var window = OpenWindow(timeline);
        try
        {
            var origin = Origin(timeline.GetClipRectangle(layer.Id)!.Value, rightEdge ? "end" : "start");
            var destination = origin + new Vector(rightEdge ? -500 : 500, 0);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            window.MouseUp(destination, MouseButton.Left);

            Assert.NotNull(committed);
            Assert.Equal(new MediaTime(1, 1000), committed.End - committed.Start);
            Assert.Equal(rightEdge ? cue.Start : cue.End - new MediaTime(1, 1000), committed.Start);
            Assert.Equal(rightEdge ? cue.Start + new MediaTime(1, 1000) : cue.End, committed.End);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void AClipShorterThanOneFrameCanStillBeTrimmedByOneMillisecond(bool rightEdge)
    {
        var cue = new SubtitleLine { Start = new(1, 10), End = new(105, 1000), Text = "Short" };
        var layer = Layer(cue);
        using var timeline = new SubtitleTimelineControl { PixelsPerSecond = 2000, IsSnapEnabled = false };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, cue.Id, layer);
        TimelineTimingEventArgs? committed = null;
        timeline.TimingChanged += (_, e) => committed = e;
        var window = OpenWindow(timeline);
        try
        {
            var origin = Origin(timeline.GetClipRectangle(layer.Id)!.Value, rightEdge ? "end" : "start");
            var destination = origin + new Vector(rightEdge ? -2 : 2, 0);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            window.MouseUp(destination, MouseButton.Left);

            Assert.NotNull(committed);
            Assert.Equal(new MediaTime(4, 1000), committed.End - committed.Start);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, 43, 100)]
    [InlineData(true, 427, 1000)]
    public void StepKeepsTheVisibleMinorDivisionAndAltTemporarilyUsesMillisecondPrecision(
        bool alt, long expectedNumerator, long expectedDenominator)
    {
        var cue = new SubtitleLine { Start = new(1, 10), End = new(2, 5), Text = "Step" };
        var layer = Layer(cue);
        using var timeline = new SubtitleTimelineControl
        {
            PixelsPerSecond = 2000, IsSnapEnabled = false, IsStepEnabled = true
        };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, cue.Id, layer);
        TimelineTimingEventArgs? committed = null;
        timeline.TimingChanged += (_, e) => committed = e;
        var window = OpenWindow(timeline);
        try
        {
            timeline.ViewStart = 0.1;
            var origin = Origin(timeline.GetClipRectangle(layer.Id)!.Value, "end");
            var destination = origin + new Vector(54, 0);
            var modifiers = alt ? RawInputModifiers.Alt : RawInputModifiers.None;
            window.MouseDown(origin, MouseButton.Left, modifiers);
            window.MouseMove(destination, modifiers);
            window.MouseUp(destination, MouseButton.Left, modifiers);

            Assert.NotNull(committed);
            Assert.Equal(new MediaTime(expectedNumerator, expectedDenominator), committed.End);
            Assert.True(timeline.IsStepEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    private static Point Origin(Rect clip, string mode) => mode switch
    {
        "start" => new(clip.Left + 1, clip.Center.Y),
        "end" => new(clip.Right - 1, clip.Center.Y),
        _ => clip.Center
    };

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };

    private static Window OpenWindow(SubtitleTimelineControl timeline)
    {
        var window = new Window { Width = 1100, Height = 240, Content = timeline };
        window.Show();
        window.UpdateLayout();
        return window;
    }
}
