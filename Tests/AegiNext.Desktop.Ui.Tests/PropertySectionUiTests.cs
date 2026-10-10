using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class PropertySectionUiTests
{
    [AvaloniaTheory]
    [InlineData(0.15, false)]
    [InlineData(0.9, false)]
    [InlineData(0.15, true)]
    [InlineData(0.9, true)]
    public void HeaderTextAndBlankAreaToggleOnceAndRetainInvalidDraft(double horizontalFraction, bool initiallyExpanded)
    {
        var input = new NumericDraftInput { RawText = "unfinished" };
        var section = new PropertySection { Header = "Typography", Content = input, IsExpanded = initiallyExpanded };
        var window = new Window
        {
            Width = 420,
            Height = 180,
            Content = new StackPanel { Margin = new(12), Children = { section } }
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            var header = section.GetVisualDescendants().OfType<ToggleButton>().Single(button => button.Name == "PART_HeaderSite");
            var point = header.TranslatePoint(new(header.Bounds.Width * horizontalFraction, header.Bounds.Height / 2), window)!.Value;
            Assert.True(window.InputHitTest(point) is Visual visual && (ReferenceEquals(visual, header) || visual.GetVisualAncestors().Contains(header)));
            window.MouseDown(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var pressedFrame = window.CaptureRenderedFrame();
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(!initiallyExpanded, section.IsExpanded);
            Assert.Equal("unfinished", input.RawText);
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var foldedFrame = window.CaptureRenderedFrame();
            window.MouseDown(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var secondPressedFrame = window.CaptureRenderedFrame();
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(initiallyExpanded, section.IsExpanded);
            Assert.Same(input, section.Content);
            Assert.Equal("unfinished", input.RawText);
        }
        finally
        {
            window.Close();
        }
    }
}
