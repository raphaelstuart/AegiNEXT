using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SkiaSharp;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitlePositionDiagramUiTests
{
    [AvaloniaFact]
    public void SharedVectorRowsKeepValidationFocusAndEscapeRestoresOnlyFocusedComponent()
    {
        using var environment = new UiTestEnvironment();
        var draft = new SubtitlePositionDraft();
        draft.Load(new() { Position = CreatePosition() }, geometry: CreateGeometry());
        var editor = new SubtitlePositionEditor { DataContext = draft };
        var window = new Window { Width = 440, Height = 760, Content = editor };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.Equal(3, editor.GetLogicalDescendants().OfType<VectorDraftInput>().Count());
            var inputs = editor.GetLogicalDescendants().OfType<NumericDraftInput>().ToArray();
            var anchor = Assert.Single(inputs, input => input.Name == "AnchorXInput");
            var pivot = Assert.Single(inputs, input => input.Name == "PivotYInput");
            var offset = Assert.Single(inputs, input => input.Name == "OffsetXInput");
            Assert.Equal(0, anchor.Minimum);
            Assert.Equal(1, anchor.Maximum);
            Assert.Equal(0.1m, anchor.Increment);
            Assert.Equal(0.1m, pivot.Increment);
            Assert.Equal(1, offset.Increment);
            draft.OffsetY.RawText = "17";
            var text = Assert.Single(anchor.GetVisualDescendants().OfType<TextBox>());
            Assert.True(text.Focus());
            text.SelectAll();
            window.KeyTextInput("7e-");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("AnchorXInput", draft.Validate());
            Assert.True(editor.FocusInvalidField("AnchorXInput"));
            Assert.True(anchor.IsFocused || text.IsFocused);

            UiTestActions.Press(window, Key.Escape);
            Dispatcher.UIThread.RunJobs();

            Assert.Null(draft.Validate());
            Assert.Equal(0.5m, anchor.Value);
            Assert.Equal("17", draft.OffsetY.RawText);
            Assert.Equal(17, draft.CreatePosition()!.Offset.Y);
            Assert.False(editor.FocusInvalidField("MissingInput"));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ActualVectorRenderShowsNaturalGlyphBoundsDistinctAnchorPivotAndOffset()
    {
        using var environment = new UiTestEnvironment();
        var diagram = CreateDiagram();
        using var pixels = Render(diagram);

        Assert.False(diagram.IsHitTestVisible);
        Assert.False(diagram.Focusable);
        AssertGreen(pixels.GetPixel(110, 45));
        var anchor = pixels.GetPixel(110, 60);
        Assert.True(anchor.Blue > 150 && anchor.Red < 100);
        var pivot = pixels.GetPixel(129, 50);
        Assert.True(pivot.Red > 150 && pivot.Green > 50 && pivot.Blue < 60);
        var offset = pixels.GetPixel(119, 55);
        Assert.True(offset.Red > 60 && offset.Green > 60 && offset.Blue > 60);
        Assert.Equal(SKColors.Black, pixels.GetPixel(100, 45));
    }

    [AvaloniaFact]
    public void ActualGlyphOutlineChangesWithNaturalMeasurementsAndRotatedScale()
    {
        using var environment = new UiTestEnvironment();
        var diagram = CreateDiagram();
        using var original = Render(diagram);
        AssertGreen(original.GetPixel(110, 45));
        diagram.Geometry = diagram.Geometry! with { GlyphSize = new(20, 40) };
        using var resized = Render(diagram);
        Assert.Equal(SKColors.Black, resized.GetPixel(110, 45));
        AssertGreen(resized.GetPixel(119, 38));

        diagram.Geometry = CreateGeometry() with { Transform = new(ScaleX: 2, Rotation: 90) };
        using var rotated = Render(diagram);
        Assert.Equal(SKColors.Black, rotated.GetPixel(110, 45));
        AssertGreen(rotated.GetPixel(119, 25));
        AssertGreen(rotated.GetPixel(138, 75));
    }

    [AvaloniaFact]
    public void SharedEditorBindsTheReadOnlyDiagramAndWrapsWithoutChangingTheDraft()
    {
        using var environment = new UiTestEnvironment();
        var geometry = CreateGeometry();
        var style = new SubtitleStyle { FontFamily = "sans-serif", Position = CreatePosition() };
        var draft = new SubtitlePositionDraft();
        draft.Load(style, geometry: geometry);
        var editor = new SubtitlePositionEditor { DataContext = draft };
        var window = new Window { Width = 480, Height = 700, Content = editor };
        var changes = 0;
        draft.Changed += (_, _) => changes++;
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            var diagram = editor.FindControl<SubtitlePositionDiagram>("PositionDiagram")!;
            var picker = editor.FindControl<AnchorPresetPicker>("AnchorPresets")!;
            Assert.Same(geometry, diagram.Geometry);
            Assert.Equal(style.Position, diagram.Position);
            Assert.False(diagram.IsHitTestVisible);
            Assert.False(diagram.Focusable);
            Assert.Equal(picker.Bounds.Y, diagram.Bounds.Y, 3);
            window.Width = 240;
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            using (var frame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(frame);
            }
            Assert.True(diagram.Bounds.Top >= picker.Bounds.Bottom);
            Assert.Equal(0, changes);
            Assert.Equal(style.Position, draft.CreatePosition());
            Assert.Equal("sans-serif", style.FontFamily);

            var updated = geometry with { GlyphSize = new(20, 40), Transform = new(Rotation: 90) };
            draft.UpdateGeometry(updated);
            Dispatcher.UIThread.RunJobs();
            Assert.Same(updated, diagram.Geometry);
            Assert.Equal(0, changes);
            Assert.Equal(style.Position, draft.CreatePosition());
        }
        finally
        {
            window.Close();
        }
    }

    private static SubtitlePositionDiagram CreateDiagram()
    {
        var diagram = new SubtitlePositionDiagram
        {
            Geometry = CreateGeometry(), Position = CreatePosition(), Foreground = Brushes.White
        };
        diagram.Measure(new(220, 140));
        diagram.Arrange(new(0, 0, 220, 140));
        return diagram;
    }

    private static SubtitlePositionGeometry CreateGeometry()
    {
        return new(new(200, 100), new(20, 30), new(40, 20), new());
    }

    private static SubtitlePosition CreatePosition()
    {
        return new() { Anchor = new(0.5, 0.5), Pivot = new(0.5, 0.5), Offset = new(20, -10) };
    }

    private static SKBitmap Render(SubtitlePositionDiagram diagram)
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

    private static void AssertGreen(SKColor color)
    {
        Assert.True(color.Green > 80 && color.Red < 90 && color.Blue < 130, $"Expected natural glyph outline, received {color}.");
    }
}
