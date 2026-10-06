using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KaraokeAxisSnappingUiTests
{
    [AvaloniaTheory]
    [InlineData(4, 1, 1.345, 1.35)]
    [InlineData(4, 1, 3.97, 4)]
    [InlineData(4, 1, 1.97, 2)]
    [InlineData(4, 1, 4.12, 4.12)]
    public void RightDurationUsesGridAndFrozenBoundariesAndCanLeaveCueEnd(double cueEnd, double originalEnd,
        double desiredEnd, double expectedEnd)
    {
        VerifyDrag(cueEnd, originalEnd, desiredEnd, expectedEnd, true);
    }

    [AvaloniaTheory]
    [InlineData(false, RawInputModifiers.None)]
    [InlineData(true, RawInputModifiers.Alt)]
    public void DisabledOrAltBypassKeepsFineDuration(bool snap, RawInputModifiers modifier)
    {
        VerifyDrag(4, 1, 1.345, 1.345, snap, modifier);
    }

    private static void VerifyDrag(double cueEnd, double originalEnd, double desiredEnd, double expectedEnd,
        bool snap, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        using var environment = new UiTestEnvironment();
        var line = new SubtitleLine
        {
            Text = "ab", End = Time(cueEnd), Karaoke =
            [new(0, 1, MediaTime.Zero, Time(originalEnd), SceneColor.White),
                new(1, 1, Time(originalEnd), new(2), SceneColor.White)]
        };
        var axis = new KaraokeClipAxis { IsSnapEnabled = snap };
        axis.ClipSelectionRequested += (_, e) => axis.SetContent(line, MediaTime.Zero, e.ClipId);
        var requests = new List<KaraokeClipDurationEventArgs>();
        axis.DurationRequested += (_, e) => requests.Add(e);
        axis.SetContent(line, MediaTime.Zero, null);
        var window = new Window { Width = 424, Height = 140, Content = axis };
        try
        {
            window.Show();
            window.UpdateLayout();
            var pixels = (axis.Bounds.Width - 24) / cueEnd;
            var point = axis.TranslatePoint(new Point(12 + originalEnd * pixels - 3, 40), window)!.Value;
            window.MouseDown(point, MouseButton.Left, modifiers);
            var moved = point + new Vector((desiredEnd - originalEnd) * pixels, 0);
            window.MouseMove(moved, modifiers);
            Assert.Empty(requests);
            window.MouseUp(moved, MouseButton.Left, modifiers);
            var request = Assert.Single(requests);
            Assert.Equal(Time(expectedEnd), request.Duration);
            Assert.Equal(line.Id, request.SubtitleId);
            Assert.Equal(line.Karaoke[0].Id, request.ClipId);
        }
        finally
        {
            window.Close();
        }
    }

    private static MediaTime Time(double seconds) => new((long)Math.Round(seconds * 1000000), 1000000);
}
