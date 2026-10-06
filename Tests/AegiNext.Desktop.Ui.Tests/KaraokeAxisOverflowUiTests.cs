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

public sealed class KaraokeAxisOverflowUiTests
{
    [AvaloniaFact]
    public void OverflowClipRemainsSelectableAndDraggableAndEarlierClipCanShrinkBackIntoCue()
    {
        using var environment = new UiTestEnvironment();
        var line = new SubtitleLine
        {
            Text = "ab", End = new(4), Karaoke =
            [new(0, 1, MediaTime.Zero, new(9), SceneColor.White), new(1, 1, new(9), new(10), SceneColor.White)]
        };
        var document = new ProjectDocument { Subtitles = [line], Layers =
            [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }] };
        var editor = new ProjectEditor(document);
        var axis = new KaraokeClipAxis();
        Guid? selected = null;
        axis.ClipSelectionRequested += (_, e) =>
        {
            selected = e.ClipId;
            axis.SetContent(editor.Snapshot.Subtitles[0], MediaTime.Zero, selected);
        };
        axis.DurationRequested += (_, e) =>
        {
            editor.SetKaraokeClipDuration(e.SubtitleId, e.ClipId, e.Duration);
            axis.SetContent(editor.Snapshot.Subtitles[0], MediaTime.Zero, selected);
        };
        axis.SetContent(line, MediaTime.Zero, null);
        var window = new Window { Width = 420, Height = 160, Content = axis };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.True(axis.HasOverflow);
            var pixels = (axis.Bounds.Width - 24) / 10;
            var endPoint = axis.TranslatePoint(new Point(12 + 9.5 * pixels, 40), window)!.Value;
            window.MouseDown(endPoint, MouseButton.Left);
            Assert.Equal(line.Karaoke[1].Id, selected);
            window.MouseMove(endPoint - new Vector(0.5 * pixels, 0));
            window.MouseUp(endPoint - new Vector(0.5 * pixels, 0), MouseButton.Left);
            Assert.Equal(new MediaTime(19, 2), editor.Snapshot.Subtitles[0].Karaoke[^1].End);
            Assert.True(axis.HasOverflow);
            window.UpdateLayout();
            pixels = (axis.Bounds.Width - 24) / 9.5;
            var firstPoint = axis.TranslatePoint(new Point(12 + 4.5 * pixels, 40), window)!.Value;
            window.MouseDown(firstPoint, MouseButton.Left);
            Assert.Equal(line.Karaoke[0].Id, selected);
            window.MouseMove(firstPoint - new Vector(7 * pixels, 0));
            window.MouseUp(firstPoint - new Vector(7 * pixels, 0), MouseButton.Left);
            Assert.Equal(new MediaTime(5, 2), editor.Snapshot.Subtitles[0].Karaoke[^1].End);
            Assert.False(axis.HasOverflow);
            Assert.Equal(line.End, editor.Snapshot.Subtitles[0].End);
            Assert.True(editor.Undo());
            Assert.True(editor.Undo());
            Assert.Same(document, editor.Snapshot);
        }
        finally
        {
            window.Close();
        }
    }
}
