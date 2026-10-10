using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KaraokeAxisLaneUiTests
{
    [Fact]
    public void OverlappingAndReverseTimedGroupsUseStableMinimalLanesRegardlessOfInputOrder()
    {
        var clips = new[]
        {
            Clip(0, 3, 5), Clip(1, 0, 4), Clip(2, 1, 2), Clip(3, 2, 3), Clip(4, 4, 5), Clip(5, 6, 7)
        };
        var lanes = KaraokeAxisLaneAllocator.Allocate(clips);
        Assert.Equal(2, lanes.Values.Max() + 1);
        Assert.Equal(0, lanes[clips[1].Id]);
        Assert.Equal(1, lanes[clips[2].Id]);
        Assert.Equal(1, lanes[clips[3].Id]);
        Assert.Equal(1, lanes[clips[0].Id]);
        Assert.Equal(0, lanes[clips[4].Id]);
        Assert.Equal(0, lanes[clips[5].Id]);
        var shuffled = KaraokeAxisLaneAllocator.Allocate(clips.Reverse());
        Assert.Equal(lanes.OrderBy(pair => pair.Key), shuffled.OrderBy(pair => pair.Key));
        foreach (var lane in clips.GroupBy(clip => lanes[clip.Id]))
        {
            var ordered = lane.OrderBy(clip => clip.Start).ToArray();
            for (var index = 1; index < ordered.Length; index++)
            {
                Assert.True(ordered[index - 1].End <= ordered[index].Start);
            }
        }
    }

    [Fact]
    public void EqualTimeIntervalsUseEndThenTextThenIdentityForDeterministicOrdering()
    {
        var first = Clip(4, 1, 3) with { Id = Guid.Parse("00000000-0000-0000-0000-000000000001") };
        var second = first with { Id = Guid.Parse("00000000-0000-0000-0000-000000000002") };
        var textBefore = Clip(3, 1, 3);
        var shorter = Clip(8, 1, 2);
        var lanes = KaraokeAxisLaneAllocator.Allocate([second, first, textBefore, shorter]);
        Assert.Equal(0, lanes[shorter.Id]);
        Assert.Equal(1, lanes[textBefore.Id]);
        Assert.Equal(2, lanes[first.Id]);
        Assert.Equal(3, lanes[second.Id]);
    }

    [AvaloniaFact]
    public void DragKeepsItsLaneUntilReleaseThenCommittedOverlapReallocatesAndVerticalPanReachesIt()
    {
        var line = new SubtitleLine { Text = "ab", End = new(4), Karaoke = [Clip(0, 0, 1), Clip(1, 2, 3)] };
        using var host = new KaraokeAxisUiTestHost(line);
        var clip = line.Karaoke[1];
        host.Axis.SetContent(line, host.Offset, clip.Id);
        var lanes = host.Axis.ClipLanes.ToDictionary();
        var point = host.Point(host.Axis.GeometryFor(clip.Id).Body.Center);
        var shift = new Vector(-2 * host.Axis.Viewport.PixelsPerSecond, 0);
        host.Window.MouseDown(point, MouseButton.Left);
        host.Window.MouseMove(point + shift);
        Assert.Equal(lanes.OrderBy(pair => pair.Key), host.Axis.ClipLanes.OrderBy(pair => pair.Key));
        Assert.Equal(22, host.Axis.GeometryFor(clip.Id).Body.Top);
        host.Window.MouseUp(point + shift, MouseButton.Left);
        var request = Assert.Single(host.Requests);
        var changed = line with { Karaoke = line.Karaoke.SetItem(1, clip with { Start = request.Start, End = request.End }) };
        host.Replace(changed, clip.Id);
        Assert.Equal(1, host.Axis.ClipLanes[clip.Id]);
        Assert.True(host.Axis.GeometryFor(clip.Id).Body.Top >= 72);
        var wheelPoint = host.Point(new(100, 40));
        host.Window.MouseWheel(wheelPoint, new(0, -3));
        Assert.True(host.Axis.Viewport.VerticalOffset > 0);
        var moved = host.Axis.GeometryFor(clip.Id);
        Assert.True(moved.Body.Center.Y < 66);
        host.Drag(moved.Body.Center, new(20, 0));
        Assert.Equal(2, host.Requests.Count);
    }

    [AvaloniaTheory]
    [InlineData(1.5, -1.5, 1.5, 0)]
    [InlineData(-1.5, 1.5, 0, 1.5)]
    public void VisibleRelativeTimeCanBeNegativeWhileViewAndStoredTimeRemainNonnegative(double offset,
        double expectedRelative, double expectedCueView, double expectedClipView)
    {
        var line = new SubtitleLine { Text = "ab", End = new(4), Karaoke = [Clip(0, 0, 1), Clip(1, 2, 3)] };
        var animationOffset = Time(offset);
        using var host = new KaraokeAxisUiTestHost(line, animationOffset);
        Assert.Equal(Time(expectedRelative), host.Axis.VisibleRelativeTime(MediaTime.Zero));
        Assert.True(host.Axis.Viewport.StartSeconds >= 0);
        var minimum = Math.Min(0, offset);
        var cueView = offset - minimum;
        Assert.Equal(expectedCueView, cueView);
        var clipView = -minimum;
        Assert.Equal(expectedClipView, clipView);
        if (offset > 0)
        {
            host.Window.MouseWheel(host.Point(new(12, 40)), new(0, 4), RawInputModifiers.Control);
            Assert.Contains(host.Axis.RulerTicks(), tick => tick.Seconds < 0 && tick.Label.StartsWith('-'));
            Assert.Contains(host.Axis.RulerTicks(), tick => tick.Seconds == 0 && tick.Label == "0 s");
        }
        var rect = host.Axis.GeometryFor(line.Karaoke[0].Id).TimeBounds;
        Assert.InRange(Math.Abs(rect.Left - (12 + clipView * host.Axis.Viewport.PixelsPerSecond)), 0, 0.00001);
        host.Drag(host.Axis.GeometryFor(line.Karaoke[0].Id).Body.Center, new(-1000, 0));
        Assert.Empty(host.Requests);
        Assert.Equal(MediaTime.Zero, line.Karaoke[0].Start);
    }

    private static KaraokeSegment Clip(int text, long start, long end) => new(text, 1, new(start), new(end), SceneColor.White);
    private static MediaTime Time(double value) => new((long)Math.Round(value * 1000000), 1000000);
}
