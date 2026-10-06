using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WorkbenchTimelineDragLifecycleUiTests
{
    [AvaloniaFact]
    public void TimelineClockKeepsCurrentGestureButAnyNewSnapshotCancelsIt()
    {
        var document = CreateDocument();
        using var timeline = new SubtitleTimelineControl();
        timeline.SetDocument(document, document.Subtitles[0].Id, document.Layers[0]);
        timeline.BeginTimingDrag(document.Subtitles[0], TimelineDragMode.TRIM_END, 20, false);
        timeline.Position = new(1, 2);
        timeline.SetDocument(document, document.Subtitles[0].Id, document.Layers[0]);
        Assert.True(timeline.HasActiveDrag);

        timeline.SetDocument(document with { Name = "Changed" }, document.Subtitles[0].Id, document.Layers[0]);
        Assert.False(timeline.HasActiveDrag);
    }

    [AvaloniaFact]
    public void TimelineKeyframeDragCannotCommitToAnotherPropertyOrSelectedLayer()
    {
        var document = CreateDocument();
        var layer = document.Layers[0];
        var key = layer.Tracks[0].Keyframes[0];
        using var timeline = new SubtitleTimelineControl();
        timeline.SetDocument(document, document.Subtitles[0].Id, layer);
        timeline.BeginKeyframeDrag(layer, key, 20);
        timeline.EffectProperty = AnimationProperty.POSITION;
        Assert.False(timeline.HasActiveDrag);

        timeline.BeginKeyframeDrag(layer, key, 20);
        timeline.SetDocument(document, document.Subtitles[1].Id, document.Layers[1]);
        Assert.False(timeline.HasActiveDrag);
    }

    [AvaloniaFact]
    public void SwitchingSelectedClipCancelsAnUncommittedGesture()
    {
        var document = CreateDocument();
        using var timeline = new SubtitleTimelineControl();
        timeline.SetDocument(document, document.Subtitles[0].Id, document.Layers[0]);
        timeline.BeginTimingDrag(document.Subtitles[0], TimelineDragMode.MOVE, 20, false);
        timeline.SetDocument(document, document.Subtitles[1].Id, document.Layers[1]);
        Assert.False(timeline.HasActiveDrag);
    }

    private static ProjectDocument CreateDocument()
    {
        var first = new SubtitleLine { Text = "first" };
        var second = new SubtitleLine { Text = "second", Start = new(2), End = new(4) };
        var path = new MotionPath(new(new(0, 0), [new(new(10, 0), new(10, 10), new(0, 10))]), first.End - first.Start);
        return new()
        {
            Subtitles = [first, second],
            Layers =
            [
                new()
                {
                    Id = first.Id, Kind = LayerKind.SUBTITLE, SubtitleId = first.Id, End = first.End, MotionPath = path,
                    Tracks = [new(AnimationProperty.OPACITY, [new(new(0), 0.5)])]
                },
                new() { Id = second.Id, Kind = LayerKind.SUBTITLE, SubtitleId = second.Id, Start = second.Start, End = second.End }
            ]
        };
    }
}
