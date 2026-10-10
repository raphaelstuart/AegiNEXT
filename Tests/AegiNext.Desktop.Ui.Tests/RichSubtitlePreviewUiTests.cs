using System.Reflection;
using System.Runtime.InteropServices;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Rendering.Projects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class RichSubtitlePreviewUiTests
{
    [AvaloniaFact]
    public void FixedModesReuseTheRenderedFrameAcrossPlaybackTimeAndRebuildWhenTheModeChanges()
    {
        using var editor = new RichSubtitleEditor();
        var document = Document();
        var line = document.Subtitles[0];
        editor.SetContent(document, line, AppContext.BaseDirectory, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED);
        var highlighted = Bitmap(editor);
        var pixels = Pixels(highlighted);
        editor.SetContent(document, line, AppContext.BaseDirectory, new(20), SubtitlePreviewMode.HIGHLIGHTED);
        Assert.Same(highlighted, Bitmap(editor));
        editor.SetContent(document, line, AppContext.BaseDirectory, new(20), SubtitlePreviewMode.NORMAL);
        var normal = Bitmap(editor);
        Assert.NotSame(highlighted, normal);
        Assert.NotEqual(pixels, Pixels(normal));
        editor.SetContent(document, line, AppContext.BaseDirectory, MediaTime.Zero, SubtitlePreviewMode.NORMAL);
        Assert.Same(normal, Bitmap(editor));
        Assert.Null(editor.RenderDiagnostic);
    }

    [AvaloniaFact]
    public void CompositionKeepsTheFixedAppearanceAndEscapeCancelsOnlyTheComposition()
    {
        using var editor = new RichSubtitleEditor { RestoreOnEscape = false };
        var window = new Window { Content = editor, Width = 900, Height = 300 };
        try
        {
            window.Show();
            var document = Document();
            editor.SetContent(document, document.Subtitles[0], AppContext.BaseDirectory,
                MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED);
            var highlighted = Pixels(Bitmap(editor));
            var restores = 0;
            editor.RestoreRequested += (_, _) => restores++;
            editor.SetSelection(0, 1);
            editor.SetPreedit("CD", 1);
            Assert.Contains(Pixels(Bitmap(editor)).Chunk(4), pixel => pixel[2] > 100 && pixel[0] < 30);
            var composed = Bitmap(editor);
            editor.SetContent(document, document.Subtitles[0], AppContext.BaseDirectory,
                new(20), SubtitlePreviewMode.HIGHLIGHTED);
            Assert.Same(composed, Bitmap(editor));
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Empty(editor.Preedit);
            Assert.Equal(highlighted, Pixels(Bitmap(editor)));
            Assert.Equal(0, restores);
            editor.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
            Assert.Equal(0, restores);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EscapeCancelsThePointerSelectionBeforeItCanEndTheEditorInput()
    {
        using var editor = new RichSubtitleEditor { RestoreOnEscape = false };
        var window = new Window { Content = editor, Width = 900, Height = 300 };
        try
        {
            window.Show();
            var document = Document();
            editor.SetContent(document, document.Subtitles[0], AppContext.BaseDirectory,
                MediaTime.Zero, SubtitlePreviewMode.NORMAL);
            window.UpdateLayout();
            IPointer? pointer = null;
            editor.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer,
                RoutingStrategies.Bubble, handledEventsToo: true);
            var exits = 0;
            window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
            {
                if (e.Key == Key.Escape)
                {
                    exits++;
                }
            }, RoutingStrategies.Bubble);
            var point = editor.TranslatePoint(new Point(13, 30), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            Assert.NotNull(pointer);
            Assert.Same(editor, pointer.Captured);
            Assert.True(editor.IsSelectionGestureActive);
            window.MouseMove(point + new Vector(100, 0));
            UiTestActions.Press(window, Key.Escape);
            Assert.Null(pointer.Captured);
            Assert.False(editor.IsSelectionGestureActive);
            Assert.Equal(0, exits);
            var selection = (editor.SelectionStart, editor.SelectionEnd);
            window.MouseMove(point);
            Assert.Equal(selection, (editor.SelectionStart, editor.SelectionEnd));
            window.MouseUp(point, MouseButton.Left);
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal(1, exits);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ActiveAndInactiveShadowsReserveTheirFullPreviewPadding()
    {
        using var editor = new RichSubtitleEditor();
        var document = Document();
        var line = document.Subtitles[0];
        editor.SetContent(document, line, AppContext.BaseDirectory, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED);
        var initialWidth = Bitmap(editor).PixelSize.Width;
        var wider = line with
        {
            KaraokeStyleSpans = [new(line.Karaoke[0].Utf16Start, line.Karaoke[0].Utf16Length,
                new() { ShadowOffset = new(80, 0), ShadowBlur = 10, ShadowColor = SceneColor.White },
                new() { ShadowOffset = new(-120, 0), ShadowColor = SceneColor.White })]
        };
        editor.SetContent(document, wider, AppContext.BaseDirectory, MediaTime.Zero, SubtitlePreviewMode.HIGHLIGHTED);
        Assert.True(Bitmap(editor).PixelSize.Width >= initialWidth + 200);
        Assert.Null(editor.RenderDiagnostic);
    }

    private static WriteableBitmap Bitmap(RichSubtitleEditor editor) => Assert.IsType<WriteableBitmap>(
        typeof(RichSubtitleEditor).GetField("bitmap", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(editor));

    private static byte[] Pixels(WriteableBitmap bitmap)
    {
        using var locked = bitmap.Lock();
        var pixels = new byte[bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4];
        for (var row = 0; row < bitmap.PixelSize.Height; row++)
        {
            Marshal.Copy(locked.Address + row * locked.RowBytes, pixels, row * bitmap.PixelSize.Width * 4,
                bitmap.PixelSize.Width * 4);
        }
        return pixels;
    }

    private static ProjectDocument Document()
    {
        var line = new SubtitleLine
        {
            Text = "AB", End = new(20), Style = new() { FontSize = 32, Fill = new(0, 0, 1), StrokeWidth = 0 },
            KaraokeStyle = KaraokeHighlightStyle.FromStyle(Guid.NewGuid(), "Red", new()
            {
                Fill = new(1, 0, 0), StrokeWidth = 0
            }),
            Karaoke = [new(0, 1, new(10), new(11), SceneColor.White), new(1, 1, new(11), new(12), SceneColor.White)]
        };
        return new() { Subtitles = [line], Layers =
            [new() { Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }] };
    }
}
