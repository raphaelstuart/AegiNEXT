using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WorkbenchDragLifecycleTests
{
    [AvaloniaFact]
    public void UndoingMotionPathDuringDragCancelsDraftBeforeTheNextPointerMove()
    {
        var document = CreateDocument();
        var layer = document.Layers[0];
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        var cancellationCount = 0;
        canvas.GestureCancelled += (_, _) => cancellationCount++;
        canvas.SetScene(document, layer, new(0));
        Assert.True(canvas.BeginDrag(new(10, 20), 0));
        Assert.True(canvas.HasActiveDrag);

        var withoutPath = layer with { MotionPath = null };
        canvas.SetScene(document with { Layers = document.Layers.SetItem(0, withoutPath) }, withoutPath, new(0));

        Assert.False(canvas.HasActiveDrag);
        Assert.Equal(1, cancellationCount);
        Assert.False(canvas.BeginDrag(new(10, 20), 0));
    }

    [AvaloniaFact]
    public void CanvasClockUpdatesPreserveDragButSwitchingLayerOrModeCancelsIt()
    {
        var document = CreateDocument();
        using var canvas = new EffectCanvasControl();
        canvas.SetScene(document, document.Layers[0], new(0));
        Assert.True(canvas.BeginDrag(new(10, 20)));
        canvas.SetScene(document, document.Layers[0], new(1, 2));
        Assert.True(canvas.HasActiveDrag);

        canvas.SetScene(document, document.Layers[1], new(1, 2));
        Assert.False(canvas.HasActiveDrag);
        Assert.True(canvas.BeginDrag(new(10, 20)));
        canvas.EditMode = CanvasEditMode.PATH;
        Assert.False(canvas.HasActiveDrag);
    }

    [AvaloniaFact]
    public void CancellingPathGestureDiscardsTheDraftWithoutCommittingAndAllowsAnotherDrag()
    {
        var document = CreateDocument();
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        var cancellationCount = 0;
        var commitCount = 0;
        canvas.GestureCancelled += (_, _) => cancellationCount++;
        canvas.LayerEdited += (_, _) => commitCount++;
        canvas.SetScene(document, document.Layers[0], new(0));
        Assert.True(canvas.BeginDrag(new(10, 20), 0));

        canvas.CancelGesture();

        Assert.False(canvas.HasActiveDrag);
        Assert.Equal(1, cancellationCount);
        Assert.Equal(0, commitCount);
        Assert.True(canvas.BeginDrag(new(10, 20), 0));
    }

    [AvaloniaFact]
    public void DisposingCanvasDuringPathDragDiscardsTheDraftWithoutCommitting()
    {
        var document = CreateDocument();
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.PATH };
        var cancellationCount = 0;
        var commitCount = 0;
        canvas.GestureCancelled += (_, _) => cancellationCount++;
        canvas.LayerEdited += (_, _) => commitCount++;
        canvas.SetScene(document, document.Layers[0], new(0));
        Assert.True(canvas.BeginDrag(new(10, 20), 0));

        canvas.Dispose();

        Assert.False(canvas.HasActiveDrag);
        Assert.Equal(1, cancellationCount);
        Assert.Equal(0, commitCount);
        Assert.False(canvas.BeginDrag(new(10, 20), 0));
    }

    [AvaloniaFact]
    public void ReplacingProjectDuringCanvasDragCannotCarryTheOldDraftIntoTheNewProject()
    {
        var document = CreateDocument();
        using var canvas = new EffectCanvasControl();
        canvas.SetScene(document, document.Layers[0], new(0));
        Assert.True(canvas.BeginDrag(new(10, 20)));
        canvas.SetScene(new(), null, new(0));
        Assert.False(canvas.HasActiveDrag);
        Assert.False(canvas.BeginDrag(new(10, 20)));
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
