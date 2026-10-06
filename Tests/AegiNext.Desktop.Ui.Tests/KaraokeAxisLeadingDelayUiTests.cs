using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KaraokeAxisLeadingDelayUiTests
{
    [AvaloniaTheory]
    [InlineData(true, RawInputModifiers.None, 0.345, 0.35)]
    [InlineData(false, RawInputModifiers.None, 0.345, 0.345)]
    [InlineData(true, RawInputModifiers.Alt, 0.345, 0.345)]
    [InlineData(true, RawInputModifiers.None, -2, 0)]
    [InlineData(true, RawInputModifiers.None, 5, 5)]
    public void FirstLeftHandleRequestsOnlyOneAbsoluteLeadingDelay(bool snapping, RawInputModifiers modifiers,
        double desiredDelay, double expectedDelay)
    {
        using var environment = new UiTestEnvironment();
        var origin = new MediaTime(1, 2);
        var line = new SubtitleLine { Text = "ab", End = new(4), Karaoke =
            [new(0, 1, new(3, 2), new(5, 2), SceneColor.White), new(1, 1, new(5, 2), new(7, 2), SceneColor.White)] };
        var axis = new KaraokeClipAxis { IsSnapEnabled = snapping };
        axis.ClipSelectionRequested += (_, e) => axis.SetContent(line, origin, e.ClipId);
        var durations = new List<KaraokeClipDurationEventArgs>();
        var delays = new List<KaraokeLeadingDelayEventArgs>();
        axis.DurationRequested += (_, e) => durations.Add(e);
        axis.LeadingDelayRequested += (_, e) => delays.Add(e);
        axis.SetContent(line, origin, null);
        var window = new Window { Width = 424, Height = 140, Content = axis };
        try
        {
            window.Show();
            window.UpdateLayout();
            var pixels = (axis.Bounds.Width - 24) / 4;
            var point = axis.TranslatePoint(new Point(12 + pixels, 40), window)!.Value;
            window.MouseDown(point, MouseButton.Left, modifiers);
            var moved = point + new Vector((desiredDelay - 1) * pixels, 0);
            window.MouseMove(moved, modifiers);
            Assert.Empty(delays);
            Assert.Empty(durations);
            Assert.Empty(axis.DurationLabels);
            Assert.Equal(88, axis.Bounds.Height);
            window.MouseUp(moved, MouseButton.Left, modifiers);
            var request = Assert.Single(delays);
            Assert.Equal(line.Id, request.SubtitleId);
            Assert.Equal(Time(expectedDelay), request.Delay);
            Assert.Empty(durations);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void CancelAndExternalCueChangeReleaseThePointerWithoutARequest()
    {
        using var environment = new UiTestEnvironment();
        var line = new SubtitleLine { Text = "ab", End = new(4), Karaoke =
            [new(0, 1, new(1), new(2), SceneColor.White), new(1, 1, new(2), new(3), SceneColor.White)] };
        var axis = new KaraokeClipAxis();
        axis.ClipSelectionRequested += (_, e) => axis.SetContent(line, MediaTime.Zero, e.ClipId);
        IPointer? pointer = null;
        axis.AddHandler(InputElement.PointerPressedEvent, (_, e) => pointer = e.Pointer, RoutingStrategies.Bubble, true);
        var delays = new List<KaraokeLeadingDelayEventArgs>();
        axis.LeadingDelayRequested += (_, e) => delays.Add(e);
        axis.SetContent(line, MediaTime.Zero, null);
        var window = new Window { Width = 424, Height = 140, Content = axis };
        try
        {
            window.Show();
            window.UpdateLayout();
            var point = axis.TranslatePoint(new Point(12 + (axis.Bounds.Width - 24) / 4, 40), window)!.Value;
            SubtitleLine?[] replacements = [null, line with { End = new(5) }, line with { Text = "cd" },
                line with { Style = line.Style with { FontSize = 48 } }, line with { Id = Guid.NewGuid() }];
            foreach (var replacement in replacements)
            {
                window.MouseDown(point, MouseButton.Left);
                Assert.NotNull(pointer);
                Assert.Same(axis, pointer.Captured);
                window.MouseMove(point + new Vector(40, 0));
                if (replacement is not null)
                {
                    axis.SetContent(replacement, MediaTime.Zero, line.Karaoke[0].Id);
                }
                else
                {
                    UiTestActions.Press(window, Key.Escape);
                }
                Assert.Null(pointer.Captured);
                window.MouseUp(point + new Vector(40, 0), MouseButton.Left);
                Assert.Empty(delays);
                axis.SetContent(line, MediaTime.Zero, null);
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void IdleAxisLeavesEscapeAvailableToTheWindowCommandRouter()
    {
        using var environment = new UiTestEnvironment();
        var axis = new KaraokeClipAxis();
        var window = new Window { Width = 424, Height = 140, Content = axis };
        var routed = false;
        window.AddHandler(InputElement.KeyDownEvent, (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                routed = true;
            }
        }, RoutingStrategies.Bubble);
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.True(axis.Focus());
            UiTestActions.Press(window, Key.Escape);
            Assert.True(routed);
        }
        finally
        {
            window.Close();
        }
    }

    private static MediaTime Time(double value) => new((long)Math.Round(value * 1000000), 1000000);
}
