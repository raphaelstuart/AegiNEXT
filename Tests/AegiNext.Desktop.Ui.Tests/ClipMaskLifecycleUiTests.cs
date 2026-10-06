using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ClipMaskLifecycleUiTests
{
    [AvaloniaTheory]
    [InlineData("switch")]
    [InlineData("undo")]
    [InlineData("detach")]
    public void ReplacingSelectionUndoOrDetachingCancelsUncommittedMaskPointerGesture(string cancellation)
    {
        var first = new SubtitleLine { End = new(5) };
        var second = new SubtitleLine { Start = new(5), End = new(10) };
        var layer = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = first.Id, End = first.End };
        var other = new ProjectLayer { Kind = LayerKind.SUBTITLE, SubtitleId = second.Id, Start = second.Start, End = second.End };
        var original = new ProjectDocument { Subtitles = [first, second], Layers = [layer, other] };
        var editor = new ProjectEditor(original);
        editor.SetClipMask(layer.Id, new RectangleClipMask { TopLeft = new(0, 0), BottomRight = new(50, 50) });
        var document = editor.Snapshot;
        using var canvas = new EffectCanvasControl { EditMode = CanvasEditMode.MASK_RECTANGLE };
        canvas.SetScene(document, document.Layers[0], new(0));
        var commits = 0;
        canvas.MaskEdited += (_, _) => commits++;
        var window = new Window { Width = 700, Height = 400, Content = canvas };
        window.Show();
        try
        {
            window.UpdateLayout();
            var point = canvas.ProjectRectangle.Center;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(80, 40));
            Assert.True(canvas.HasActiveDrag);
            if (cancellation == "switch")
            {
                canvas.SetScene(document, other, new(0));
            }
            else if (cancellation == "undo")
            {
                Assert.True(editor.Undo());
                canvas.SetScene(editor.Snapshot, editor.Snapshot.Layers[0], new(0));
            }
            else
            {
                window.Content = null;
                window.Content = canvas;
                window.UpdateLayout();
            }
            window.MouseUp(point + new Vector(80, 40), MouseButton.Left);
            Assert.False(canvas.HasActiveDrag);
            Assert.Equal(0, commits);
            var expected = cancellation == "undo" ? original : document;
            Assert.Same(expected, editor.Snapshot);
        }
        finally
        {
            window.Close();
        }
    }
}
