using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectScriptLineNumbersUiTests
{
    [AvaloniaTheory]
    [InlineData("", 1)]
    [InlineData("plain", 1)]
    [InlineData("plain\n", 2)]
    [InlineData("plain\r\nnext\r\n", 3)]
    [InlineData("plain\rnext\r", 3)]
    public void EveryNativeLineIncludingTheTrailingEmptyLineHasAnAlignedNumber(string source, int count)
    {
        var editor = new EffectScriptEditor { Source = source };
        var window = CreateWindow(editor);
        try
        {
            window.Show();
            Flush(window);
            var presenter = Presenter(editor);
            Assert.Equal(count, presenter.TextLayout.TextLines.Count);
            AssertAlignedNumbers(window, editor, count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void NativeWheelScrollingMovesNumbersVerticallyAndHorizontalScrollingKeepsTheGutterFixed()
    {
        var editor = new EffectScriptEditor
        {
            Source = string.Join('\n', Enumerable.Range(1, 120).Select(index => $"plain {index} " + new string('x', 180)))
        };
        var window = CreateWindow(editor);
        try
        {
            window.Show();
            Flush(window);
            var input = UiTestActions.Find<TextBox>(window, "ScriptTextInput");
            var scroll = Scroll(input);
            var margin = Margin(editor);
            var origin = margin.TranslatePoint(default, window)!.Value;
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            Assert.True(scroll.Extent.Width > scroll.Viewport.Width);
            var wheelPoint = scroll.TranslatePoint(new Point(scroll.Bounds.Width / 2, scroll.Bounds.Height / 2), window)!.Value;
            window.MouseWheel(wheelPoint, new(0, -3));
            Flush(window);
            Assert.True(scroll.Offset.Y > 0);
            Assert.Equal(origin, margin.TranslatePoint(default, window)!.Value);
            AssertVisibleNumbersFollowNativeLines(editor);
            using var before = Render(margin);
            scroll.Offset = new(80, scroll.Offset.Y);
            Flush(window);
            Assert.True(scroll.Offset.X > 0);
            Assert.Equal(origin, margin.TranslatePoint(default, window)!.Value);
            using var after = Render(margin);
            Assert.Equal(AlphaPixels(before), AlphaPixels(after));
            AssertVisibleNumbersFollowNativeLines(editor);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void RealNewlineInsertionDeletionAndDigitGrowthUpdateTheNativeLineNumbers()
    {
        var editor = new EffectScriptEditor { Source = string.Join('\n', Enumerable.Repeat("plain", 9)) };
        var window = CreateWindow(editor, 420);
        try
        {
            window.Show();
            Flush(window);
            var input = UiTestActions.Find<TextBox>(window, "ScriptTextInput");
            Assert.True(input.Focus());
            input.CaretIndex = input.Text!.Length;
            var oneDigitWidth = Margin(editor).Bounds.Width;
            AssertAlignedNumbers(window, editor, 9);
            UiTestActions.Press(window, Key.Enter);
            Flush(window);
            AssertAlignedNumbers(window, editor, 10);
            var twoDigitWidth = Margin(editor).Bounds.Width;
            Assert.True(twoDigitWidth > oneDigitWidth);
            UiTestActions.Press(window, Key.Back);
            Flush(window);
            AssertAlignedNumbers(window, editor, 9);
            Assert.Equal(oneDigitWidth, Margin(editor).Bounds.Width);
            input.Text = string.Join('\n', Enumerable.Repeat("plain", 100));
            input.CaretIndex = 0;
            Scroll(input).Offset = default;
            Flush(window);
            Assert.True(Margin(editor).Bounds.Width > twoDigitWidth);
            AssertVisibleNumbersFollowNativeLines(editor);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ImePreviewAndTemplateReattachmentUseTheCurrentPresenterWithoutChangingCommittedSource()
    {
        var editor = new EffectScriptEditor { Source = "plain\r\nnext" };
        var window = CreateWindow(editor);
        try
        {
            window.Show();
            Flush(window);
            var input = UiTestActions.Find<TextBox>(window, "ScriptTextInput");
            var presenter = Presenter(editor);
            presenter.CaretIndex = 2;
            presenter.PreeditText = "字幕\n仮";
            Flush(window);
            Assert.Equal("plain\r\nnext", editor.Source);
            AssertAlignedNumbers(window, editor, 3);
            presenter.PreeditText = null;
            Flush(window);
            AssertAlignedNumbers(window, editor, 2);
            var oldMargin = Margin(editor);
            var template = input.Template;
            input.Template = null;
            Flush(window);
            input.Template = template;
            Flush(window);
            Assert.NotSame(oldMargin, Margin(editor));
            Assert.NotSame(presenter, Presenter(editor));
            window.Content = null;
            editor.Source = "changed\nplain\n";
            window.Content = editor;
            Flush(window);
            AssertAlignedNumbers(window, editor, 3);
            presenter.PreeditText = "old\nold\nold";
            Flush(window);
            AssertAlignedNumbers(window, editor, 3);
            Assert.Equal("changed\nplain\n", editor.Source);
        }
        finally
        {
            window.Close();
        }
    }

    private static Window CreateWindow(EffectScriptEditor editor, double height = 260)
    {
        return new() { Width = 420, Height = height, Content = editor };
    }

    private static Control Margin(EffectScriptEditor editor)
    {
        return Assert.Single(editor.GetVisualDescendants().OfType<Control>(), control => control.Name == "ScriptLineNumbers");
    }

    private static EffectScriptTextPresenter Presenter(EffectScriptEditor editor)
    {
        return Assert.Single(editor.GetVisualDescendants().OfType<EffectScriptTextPresenter>());
    }

    private static ScrollViewer Scroll(TextBox input)
    {
        return Assert.Single(input.GetVisualDescendants().OfType<ScrollViewer>(), control => control.Name == "PART_ScrollViewer");
    }

    private static void AssertAlignedNumbers(Window window, EffectScriptEditor editor, int count)
    {
        Flush(window);
        var margin = Margin(editor);
        Assert.True(margin.Bounds.Width > 0);
        Assert.False(margin.IsHitTestVisible);
        Assert.Equal(count, Presenter(editor).TextLayout.TextLines.Count);
        using var pixels = Render(margin);
        Assert.Equal(count, InkBands(pixels).Count);
        AssertVisibleNumbersFollowNativeLines(editor);
    }

    private static void AssertVisibleNumbersFollowNativeLines(EffectScriptEditor editor)
    {
        var margin = Margin(editor);
        var presenter = Presenter(editor);
        var scroll = Scroll(UiTestActions.Find<TextBox>(editor, "ScriptTextInput"));
        var origin = presenter.TranslatePoint(default, margin)!.Value;
        var viewportTop = origin.Y + scroll.Offset.Y;
        using var pixels = Render(margin);
        var bands = InkBands(pixels);
        Assert.NotEmpty(bands);
        var top = origin.Y;
        var visibleLines = new List<Rect>();
        foreach (var line in presenter.TextLayout.TextLines)
        {
            var bounds = new Rect(0, top, margin.Bounds.Width, line.Height);
            if (bounds.Top >= viewportTop && bounds.Bottom <= viewportTop + scroll.Viewport.Height)
            {
                visibleLines.Add(bounds);
            }

            top += line.Height;
        }

        foreach (var bounds in visibleLines)
        {
            Assert.Single(bands, band => band.Start >= bounds.Top && band.End < bounds.Bottom);
        }

        Assert.All(bands, band => Assert.InRange((band.Start + band.End) / 2d,
            Math.Max(0, viewportTop), Math.Min(margin.Bounds.Height, viewportTop + scroll.Viewport.Height)));
    }

    private static SKBitmap Render(Control control)
    {
        using var target = new RenderTargetBitmap(new((int)Math.Ceiling(control.Bounds.Width),
            (int)Math.Ceiling(control.Bounds.Height)), new(96, 96));
        target.Render(control);
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }

    private static List<(int Start, int End)> InkBands(SKBitmap bitmap)
    {
        var bands = new List<(int Start, int End)>();
        var start = -1;
        for (var y = 0; y < bitmap.Height; y++)
        {
            var ink = false;
            for (var x = 0; x < bitmap.Width; x++)
            {
                if (bitmap.GetPixel(x, y).Alpha > 0)
                {
                    ink = true;
                    break;
                }
            }

            if (ink && start < 0)
            {
                start = y;
            }
            else if (!ink && start >= 0)
            {
                bands.Add((start, y - 1));
                start = -1;
            }
        }

        if (start >= 0)
        {
            bands.Add((start, bitmap.Height - 1));
        }

        return bands;
    }

    private static byte[] AlphaPixels(SKBitmap bitmap)
    {
        var pixels = new byte[bitmap.Width * bitmap.Height];
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                pixels[y * bitmap.Width + x] = bitmap.GetPixel(x, y).Alpha;
            }
        }

        return pixels;
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
}
