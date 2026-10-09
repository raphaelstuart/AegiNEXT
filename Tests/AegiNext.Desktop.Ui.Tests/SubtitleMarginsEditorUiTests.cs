using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleMarginsEditorUiTests
{
    [AvaloniaFact]
    public void SharedInputsKeepInvalidRawTextAndEscapeRestoresOnlyFocusedEdge()
    {
        using var environment = new UiTestEnvironment();
        var draft = new SubtitleMarginsDraft();
        draft.Load(new(0.12500000000000003, 39, 13));
        var editor = new SubtitleMarginsEditor { Draft = draft, DataContext = new object() };
        var window = new Window { Width = 260, Height = 480, Content = editor };
        try
        {
            Show(window);
            var left = editor.FindControl<NumericDraftInput>("MarginLeftInput")!;
            var right = editor.FindControl<NumericDraftInput>("MarginRightInput")!;
            var vertical = editor.FindControl<NumericDraftInput>("MarginVerticalInput")!;
            Assert.Equal(0, left.Minimum);
            Assert.Equal(32768, left.Maximum);
            Assert.Equal(1, left.Increment);
            Assert.True(left.PreserveDoublePrecision);
            Assert.All(new[] { left, right, vertical }, input => Assert.True(input.Bounds.Width >= 64));
            Assert.True(left.Bounds.Width <= editor.Bounds.Width);
            draft.Right.RawText = "7e-";
            draft.Vertical.RawText = "17";
            var text = Assert.Single(left.GetVisualDescendants().OfType<TextBox>());
            Assert.True(text.Focus());
            text.SelectAll();
            window.KeyTextInput("7e-");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("MarginLeftInput", draft.Validate());
            Assert.True(editor.FocusInvalidField("MarginLeftInput"));

            UiTestActions.Press(window, Key.Escape);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("MarginRightInput", draft.Validate());
            Assert.Equal("7e-", draft.Right.RawText);
            Assert.Equal("17", draft.Vertical.RawText);
            Assert.Equal(0.12500000000000003.ToString("R", CultureInfo.CurrentCulture), draft.Left.RawText);
            Assert.False(editor.FocusInvalidField("MissingInput"));
            Assert.Null(editor.FindControl<SubtitleMarginsDiagram>("MarginsDiagram")!.Margins);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void EditorReplacesOnlyItsInnerDraftAndLiveLanguagePreservesRawInputs()
    {
        using var environment = new UiTestEnvironment();
        var hostContext = new object();
        var draft = new SubtitleMarginsDraft();
        draft.Load(new(17, 39, 13));
        var editor = new SubtitleMarginsEditor
        {
            Draft = draft, DataContext = hostContext, Alignment = 4, CanvasWidth = 640, CanvasHeight = 360
        };
        var window = new Window { Width = 260, Height = 480, Content = editor };
        var changes = 0;
        draft.Changed += (_, _) => changes++;
        try
        {
            Show(window);
            var diagram = editor.FindControl<SubtitleMarginsDiagram>("MarginsDiagram")!;
            Assert.Same(hostContext, editor.DataContext);
            Assert.Equal(new SubtitleMargins(17, 39, 13), diagram.Margins);
            Assert.Equal(640, diagram.CanvasWidth);
            Assert.Equal(360, diagram.CanvasHeight);
            var vertical = editor.FindControl<NumericDraftInput>("MarginVerticalInput")!;
            Assert.Contains(Localization.Get("Workbench.SubtitleMarginsMiddleHint"), Assert.IsType<string>(ToolTip.GetTip(vertical)));
            Assert.True(vertical.IsEffectivelyEnabled);
            Assert.Null(editor.FindControl<TextBlock>("MiddleMarginHint"));
            Assert.Null(editor.FindControl<TextBlock>("ExplicitMarginHint"));
            Assert.Null(editor.FindControl<TextBlock>("DiagramHint"));
            Assert.Equal("Left", editor.FindControl<TextBlock>("MarginLeftLabel")!.Text);
            draft.Left.RawText = "7e-";

            Localization.SetLanguage("zh-CN");
            Dispatcher.UIThread.RunJobs();

            Assert.Equal("左", editor.FindControl<TextBlock>("MarginLeftLabel")!.Text);
            Assert.Equal("7e-", draft.Left.RawText);
            Assert.Null(diagram.Margins);
            Assert.Equal(1, changes);
            editor.IsExplicit = true;
            Assert.False(vertical.IsEffectivelyEnabled);
            Assert.True(editor.FindControl<NumericDraftInput>("MarginLeftInput")!.IsEffectivelyEnabled);
            Assert.True(editor.FindControl<NumericDraftInput>("MarginRightInput")!.IsEffectivelyEnabled);
            Assert.Contains(Localization.Get("Workbench.SubtitleMarginsExplicitHint"), Assert.IsType<string>(ToolTip.GetTip(vertical)));
            Assert.Equal(1, changes);
            editor.ShowDiagram = false;
            Assert.False(diagram.IsVisible);
            var replacement = new SubtitleMarginsDraft();
            replacement.Load(new(3, 5, 7));
            editor.Draft = replacement;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new SubtitleMargins(3, 5, 7), diagram.Margins);
            Assert.Equal("3", editor.FindControl<NumericDraftInput>("MarginLeftInput")!.RawText);
            Assert.Same(hostContext, editor.DataContext);
            Assert.Equal("7e-", draft.Left.RawText);
            Assert.False(diagram.IsHitTestVisible);
            Assert.False(diagram.Focusable);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void HandledEscapeFromHostDoesNotRestoreTheDraftTwice()
    {
        using var environment = new UiTestEnvironment();
        var draft = new SubtitleMarginsDraft();
        var editor = new SubtitleMarginsEditor { Draft = draft };
        var window = new Window { Width = 260, Height = 480, Content = editor };
        window.AddHandler(InputElement.KeyDownEvent, (_, args) =>
        {
            if (args.Key == Key.Escape)
            {
                draft.LoadField("MarginLeftInput", 23);
                args.Handled = true;
            }
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        try
        {
            Show(window);
            draft.Left.RawText = "7e-";
            var changes = 0;
            draft.Changed += (_, _) => changes++;
            Assert.True(editor.FocusInvalidField("MarginLeftInput"));

            UiTestActions.Press(window, Key.Escape);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(1, changes);
            Assert.Equal(23, draft.CreateMargins().Left);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1, 90, 34)]
    [InlineData(7, 90, 106)]
    [InlineData(4, 90, 70)]
    [InlineData(0, 52, 34)]
    [InlineData(2, 128, 34)]
    public void RenderedLineBoxUsesInnerRegionAndActiveVerticalEdge(int alignment, int x, int y)
    {
        using var environment = new UiTestEnvironment();
        var diagram = CreateDiagram(alignment, new(20, 60, 10));
        using var pixels = Render(diagram);

        Assert.True(pixels.GetPixel(x, y).Blue > 40, $"Expected line box at ({x}, {y}).");
        Assert.Equal(0, pixels.GetPixel(20, 40).Blue);
        Assert.True(pixels.GetPixel(20, 40).Red > 30);
        Assert.True(pixels.GetPixel(180, 40).Red > 30);
        Assert.False(diagram.IsHitTestVisible);
        Assert.False(diagram.Focusable);
    }

    [AvaloniaFact]
    public void MiddleIgnoresVerticalAndExplicitRetainsOnlyHorizontalWrappingRegions()
    {
        using var environment = new UiTestEnvironment();
        var middle = CreateDiagram(4, new(20, 60, 10));
        using var first = Render(middle);
        middle.Margins = new(20, 60, 45);
        using var second = Render(middle);
        Assert.Equal(first.Bytes, second.Bytes);
        middle.IsExplicit = true;
        using var explicitPixels = Render(middle);
        Assert.Equal(SKColors.Black, explicitPixels.GetPixel(90, 70));
        Assert.True(explicitPixels.GetPixel(20, 40).Red > 30);
        Assert.True(explicitPixels.GetPixel(180, 40).Red > 30);
    }

    [AvaloniaTheory]
    [InlineData(-1)]
    [InlineData(9)]
    public void InvalidAlignmentOrMarginsCannotRenderAnEffectiveLayout(int alignment)
    {
        using var environment = new UiTestEnvironment();
        var diagram = CreateDiagram(alignment, new(20, 60, 10));
        using var invalidAlignment = Render(diagram);
        Assert.Equal(SKColors.Black, invalidAlignment.GetPixel(20, 40));
        Assert.Equal(SKColors.Black, invalidAlignment.GetPixel(90, 106));
        diagram.Alignment = 7;
        diagram.Margins = new(double.NaN, 60, 10);
        using var invalidMargins = Render(diagram);
        Assert.Equal(invalidAlignment.Bytes, invalidMargins.Bytes);
        diagram.Margins = null;
        using var missingMargins = Render(diagram);
        Assert.Equal(invalidMargins.Bytes, missingMargins.Bytes);
    }

    [AvaloniaTheory]
    [InlineData(1, 0, 18)]
    [InlineData(7, 121, 140)]
    public void ZeroMarginAxisLabelsRemainOutsideTheCanvasAndInsideTheControl(int alignment, int top, int bottom)
    {
        using var environment = new UiTestEnvironment();
        var diagram = CreateDiagram(alignment, new(0, 0, 0));
        using var pixels = Render(diagram);

        Assert.True(HasForeground(pixels, 0, 60, 8, 80));
        Assert.True(HasForeground(pixels, 213, 60, 220, 80));
        Assert.True(HasForeground(pixels, 100, top, 120, bottom));
        Assert.Equal(SKColors.Black, pixels.GetPixel(20, 70));
        Assert.Equal(SKColors.Black, pixels.GetPixel(200, 70));
    }

    private static bool HasForeground(SKBitmap pixels, int left, int top, int right, int bottom)
    {
        for (var y = top; y < bottom; y++)
        {
            for (var x = left; x < right; x++)
            {
                var color = pixels.GetPixel(x, y);
                if (color.Red > 100 && color.Green > 100 && color.Blue > 100)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static void Show(Window window)
    {
        window.Show();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
    }

    private static SubtitleMarginsDiagram CreateDiagram(int alignment, SubtitleMargins margins)
    {
        var diagram = new SubtitleMarginsDiagram
        {
            CanvasWidth = 200, CanvasHeight = 100, Margins = margins, Alignment = alignment,
            Foreground = Brushes.White, CanvasBrush = Brushes.Black, MarginBrush = Brushes.Red, MarkerBrush = Brushes.Blue
        };
        diagram.Measure(new(220, 140));
        diagram.Arrange(new(0, 0, 220, 140));
        return diagram;
    }

    private static SKBitmap Render(SubtitleMarginsDiagram diagram)
    {
        using var target = new RenderTargetBitmap(new(220, 140), new(96, 96));
        using (var drawing = target.CreateDrawingContext())
        {
            drawing.DrawRectangle(Brushes.Black, null, new Rect(0, 0, 220, 140));
            diagram.Render(drawing);
        }
        using var stream = new MemoryStream();
        target.Save(stream, PngBitmapEncoderOptions.Default);
        stream.Position = 0;
        return SKBitmap.Decode(stream);
    }
}
