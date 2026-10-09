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

public sealed class TimelinePlayheadSnappingUiTests
{
    [AvaloniaTheory]
    [InlineData("moveStart", false)]
    [InlineData("moveEnd", false)]
    [InlineData("start", false)]
    [InlineData("end", false)]
    [InlineData("moveStart", true)]
    [InlineData("moveEnd", true)]
    [InlineData("start", true)]
    [InlineData("end", true)]
    public void ClipEdgesSnapToTheExactPlayheadAfterOptionalStepQuantization(string mode, bool step)
    {
        var cue = new SubtitleLine { Start = new(1, 10), End = new(3, 10), Text = "Playhead" };
        var layer = Layer(cue);
        var playhead = new MediaTime(mode is "start" or "moveStart" ? 1117 : 3117, 10000);
        using var timeline = new SubtitleTimelineControl
        {
            PixelsPerSecond = 2000, IsSnapEnabled = true, IsStepEnabled = step, Position = playhead
        };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, cue.Id, layer);
        TimelineTimingEventArgs? committed = null;
        var seeks = 0;
        timeline.TimingChanged += (_, e) => committed = e;
        timeline.SeekRequested += (_, _) => seeks++;
        var window = OpenWindow(timeline);
        try
        {
            var origin = Origin(timeline.GetClipRectangle(layer.Id)!.Value, mode);
            var destination = origin + new Vector(24, 0);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            Assert.Equal(playhead, timeline.SnapTarget);
            window.MouseUp(destination, MouseButton.Left);

            Assert.NotNull(committed);
            var originalEdge = mode is "start" or "moveStart" ? cue.Start : cue.End;
            var offset = playhead - originalEdge;
            Assert.Equal(mode == "end" ? cue.Start : cue.Start + offset, committed.Start);
            Assert.Equal(mode == "start" ? cue.End : cue.End + offset, committed.End);
            Assert.Equal(mode.StartsWith("move", StringComparison.Ordinal), committed.IsMove);
            Assert.Equal(playhead, timeline.Position);
            Assert.Null(timeline.SnapTarget);
            Assert.Equal(0, seeks);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("moveEnd", "disabled")]
    [InlineData("moveEnd", "alt")]
    [InlineData("moveEnd", "outside")]
    [InlineData("end", "disabled")]
    [InlineData("end", "alt")]
    [InlineData("end", "outside")]
    public void PlayheadSnapHonorsTheSavedToggleAltAndPixelDistance(string mode, string bypass)
    {
        var cue = new SubtitleLine { Start = new(1, 10), End = new(3, 10), Text = "Bypass" };
        var layer = Layer(cue);
        using var timeline = new SubtitleTimelineControl
        {
            PixelsPerSecond = 2000, IsSnapEnabled = bypass != "disabled",
            Position = bypass == "outside" ? new(317, 1000) : new(3117, 10000)
        };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, cue.Id, layer);
        TimelineTimingEventArgs? committed = null;
        timeline.TimingChanged += (_, e) => committed = e;
        var window = OpenWindow(timeline);
        try
        {
            var origin = Origin(timeline.GetClipRectangle(layer.Id)!.Value, mode);
            var destination = origin + new Vector(24, 0);
            var modifiers = bypass == "alt" ? RawInputModifiers.Alt : RawInputModifiers.None;
            window.MouseDown(origin, MouseButton.Left, modifiers);
            window.MouseMove(destination, modifiers);
            Assert.Null(timeline.SnapTarget);
            window.MouseUp(destination, MouseButton.Left, modifiers);

            Assert.NotNull(committed);
            Assert.Equal(new MediaTime(312, 1000), committed.End);
            Assert.Equal(mode == "end" ? cue.Start : new MediaTime(112, 1000), committed.Start);
            Assert.Equal(bypass != "disabled", timeline.IsSnapEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("moveEnd")]
    [InlineData("start")]
    [InlineData("end")]
    public void TheNextPointerUpdateUsesTheCurrentPlayheadDuringAnActiveGesture(string mode)
    {
        var cue = new SubtitleLine { Start = new(1, 10), End = new(3, 10), Text = "Live clock" };
        var layer = Layer(cue);
        var originalEdge = mode == "start" ? cue.Start : cue.End;
        var firstPlayhead = originalEdge + new MediaTime(117, 10000);
        var currentPlayhead = originalEdge + new MediaTime(144, 10000);
        using var timeline = new SubtitleTimelineControl
        {
            PixelsPerSecond = 2000, IsSnapEnabled = true, Position = firstPlayhead
        };
        timeline.SetDocument(new() { Subtitles = [cue], Layers = [layer] }, cue.Id, layer);
        TimelineTimingEventArgs? committed = null;
        timeline.TimingChanged += (_, e) => committed = e;
        var window = OpenWindow(timeline);
        try
        {
            var origin = Origin(timeline.GetClipRectangle(layer.Id)!.Value, mode);
            var destination = origin + new Vector(24, 0);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            Assert.Equal(firstPlayhead, timeline.SnapTarget);
            timeline.Position = currentPlayhead;
            Assert.True(timeline.HasActiveDrag);
            window.MouseMove(destination + new Vector(0, 1));
            Assert.Equal(currentPlayhead, timeline.SnapTarget);
            window.MouseUp(destination + new Vector(0, 1), MouseButton.Left);

            Assert.NotNull(committed);
            Assert.Equal(currentPlayhead, mode == "start" ? committed.Start : committed.End);
            Assert.Equal(currentPlayhead, timeline.Position);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PlayheadAndOtherClipBoundariesCompeteByTheirActualPixelDistance(bool rightEdge, bool playheadCloser)
    {
        var cue = new SubtitleLine { Start = new(1, 10), End = new(3, 10), Text = "Moving" };
        var otherTrack = new ProjectTrack { Name = "Other" };
        var playhead = new MediaTime(playheadCloser ? 313 : 315, 1000);
        var neighbor = new SubtitleLine
        {
            Start = new(playheadCloser ? 315 : 313, 1000), End = new(2, 5), Text = "Neighbor"
        };
        var layer = Layer(cue);
        var expected = playheadCloser ? playhead : neighbor.Start;
        using var timeline = new SubtitleTimelineControl
        {
            PixelsPerSecond = 2000, IsSnapEnabled = true, Position = playhead
        };
        timeline.SetDocument(new()
        {
            Tracks = [ProjectTrack.Default, otherTrack], Subtitles = [cue, neighbor], Layers = [layer, Layer(neighbor) with { TrackId = otherTrack.Id }]
        }, cue.Id, layer);
        TimelineTimingEventArgs? committed = null;
        timeline.TimingChanged += (_, e) => committed = e;
        var window = OpenWindow(timeline);
        try
        {
            var origin = Origin(timeline.GetClipRectangle(layer.Id)!.Value, rightEdge ? "end" : "moveEnd");
            var destination = origin + new Vector(24, 0);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            Assert.Equal(expected, timeline.SnapTarget);
            window.MouseUp(destination, MouseButton.Left);

            Assert.NotNull(committed);
            Assert.Equal(expected, committed.End);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("valid")]
    [InlineData("cancel")]
    [InlineData("zero")]
    [InlineData("collision")]
    public void MultiClipPlayheadSnapPreservesSpacingAndExistingCommitConstraints(string outcome)
    {
        var first = new SubtitleLine { Start = new(1, 10), End = new(1, 5), Text = "First" };
        var second = new SubtitleLine { Start = new(1, 4), End = new(35, 100), Text = "Second" };
        var obstacle = new SubtitleLine { Start = new(1, 2), End = new(3, 5), Text = "Obstacle" };
        var firstLayer = Layer(first);
        var secondLayer = Layer(second);
        var source = new ProjectDocument
        {
            Subtitles = outcome == "collision" ? [first, second, obstacle] : [first, second],
            Layers = outcome == "collision" ? [firstLayer, secondLayer, Layer(obstacle)] : [firstLayer, secondLayer]
        };
        var editor = new ProjectEditor(source);
        var grabbed = outcome == "zero" ? secondLayer : firstLayer;
        using var timeline = new SubtitleTimelineControl
        {
            PixelsPerSecond = 2000, IsSnapEnabled = true,
            Position = outcome switch
            {
                "zero" => new(148, 1000),
                "collision" => new(4117, 10000),
                _ => new(2117, 10000)
            }
        };
        timeline.SetDocument(source, grabbed.SubtitleId, grabbed, [firstLayer.Id, secondLayer.Id]);
        var commits = 0;
        timeline.ClipsMoveCompleted += (_, e) =>
        {
            commits++;
            editor.ShiftClips(e.LayerIds, e.Offset);
        };
        timeline.TimingChanged += (_, _) => Assert.Fail("A selected batch must commit one shared move.");
        var window = OpenWindow(timeline);
        try
        {
            var origin = timeline.GetClipRectangle(grabbed.Id)!.Value.Center;
            var deltaPixels = outcome switch
            {
                "zero" => -198,
                "collision" => 424,
                _ => 24
            };
            var destination = origin + new Vector(deltaPixels, 0);
            window.MouseDown(origin, MouseButton.Left);
            window.MouseMove(destination);
            Assert.Same(source, editor.Snapshot);
            Assert.False(editor.CanUndo);
            if (outcome is "zero" or "collision")
            {
                Assert.Null(timeline.SnapTarget);
            }
            else
            {
                Assert.Equal(timeline.Position, timeline.SnapTarget);
            }

            if (outcome == "cancel")
            {
                timeline.CancelGesture();
                Assert.False(timeline.HasActiveDrag);
            }
            window.MouseUp(destination, MouseButton.Left);
            Assert.False(timeline.HasActiveDrag);
            Assert.Null(timeline.SnapTarget);
            if (outcome is "cancel" or "collision")
            {
                Assert.Equal(0, commits);
                Assert.Same(source, editor.Snapshot);
                Assert.False(editor.CanUndo);
            }
            else
            {
                Assert.Equal(1, commits);
                var offset = outcome == "zero" ? new MediaTime(-1, 10) : new MediaTime(117, 10000);
                Assert.Equal(first with { Start = first.Start + offset, End = first.End + offset }, editor.Snapshot.Subtitles[0]);
                Assert.Equal(second with { Start = second.Start + offset, End = second.End + offset }, editor.Snapshot.Subtitles[1]);
                Assert.True(editor.Undo());
                Assert.Same(source, editor.Snapshot);
                Assert.False(editor.CanUndo);
                Assert.True(editor.Redo());
                Assert.Equal(second.Start + offset, editor.Snapshot.Subtitles[1].Start);
            }
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
        var window = new Window { Width = 1100, Height = 340, Content = timeline };
        window.Show();
        window.UpdateLayout();
        return window;
    }
}
