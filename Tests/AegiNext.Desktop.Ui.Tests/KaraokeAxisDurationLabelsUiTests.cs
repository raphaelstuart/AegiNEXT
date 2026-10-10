using System.Globalization;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Material.Icons;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KaraokeAxisDurationLabelsUiTests
{
    [AvaloniaFact]
    public void DenseLabelsKeepEveryActualDurationCenteredAndUseSeparateRowsWithoutMovingTheAxis()
    {
        using var environment = new UiTestEnvironment();
        var text = "abcdefgh";
        var line = new SubtitleLine { Text = text, End = new(4), Karaoke =
            [.. Enumerable.Range(0, text.Length).Select(index => new KaraokeSegment(index, 1,
                new(index, 50), new(index + 1, 50), SceneColor.White))] };
        var axis = new KaraokeClipAxis { IsSnapEnabled = false };
        Assert.False(axis.KeepDurationLabelsVisible);
        axis.SelectionRequested += (_, e) => axis.SetContent(line, MediaTime.Zero, e.PrimaryClipId, e.SelectedClipIds);
        axis.SetContent(line, MediaTime.Zero, line.Karaoke[5].Id);
        var window = new Window { Width = 300, Height = 300, Content = new StackPanel
            { Children = { new Border { Height = 150 }, axis } } };
        try
        {
            window.Show();
            window.UpdateLayout();
            var origin = axis.TranslatePoint(default, window)!.Value;
            var pixels = (axis.Bounds.Width - 24) / 4;
            var point = axis.TranslatePoint(axis.GeometryFor(line.Karaoke[5].Id).EndHandle.Center, window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(20, 0));
            window.UpdateLayout();
            Assert.Equal(text.Length, axis.DurationLabels.Count);
            Assert.Equal(origin, axis.TranslatePoint(default, window));
            Assert.Equal(88, axis.Bounds.Height);
            Assert.True(axis.DurationLabels.Select(label => label.Bounds.Y).Distinct().Count() > 1);
            var delta = new MediaTime((long)Math.Round(20 / pixels * TimeSpan.TicksPerSecond), TimeSpan.TicksPerSecond);
            for (var index = 0; index < text.Length; index++)
            {
                var label = axis.DurationLabels.Single(item => item.ClipId == line.Karaoke[index].Id);
                var expected = line.Karaoke[index].End - line.Karaoke[index].Start + (index == 5 ? delta : MediaTime.Zero);
                Assert.Equal(expected, label.Duration);
                var start = line.Karaoke[index].Start;
                var end = line.Karaoke[index].End + (index == 5 ? delta : MediaTime.Zero);
                var center = 12 + (Seconds(start) + Seconds(end)) / 2 * pixels;
                Assert.InRange(Math.Abs(label.Bounds.Center.X - center), 0, 0.001);
                Assert.True(label.Bounds.Width > 8);
            }
            foreach (var group in axis.DurationLabels.GroupBy(label => label.Bounds.Y))
            {
                var sorted = group.OrderBy(label => label.Bounds.Left).ToArray();
                for (var index = 1; index < sorted.Length; index++)
                {
                    Assert.True(sorted[index - 1].Bounds.Right + 4 <= sorted[index].Bounds.Left);
                }
            }
            Assert.NotNull(AdornerLayer.GetAdornerLayer(axis));
            Capture(window, "karaoke-duration-labels-dense.png");
            window.MouseUp(point + new Vector(20, 0), MouseButton.Left);
            Assert.Empty(axis.DurationLabels);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task DenseLabelsRenderInTheActualDetailsPanelWithoutCommittingDuringTheGesture()
    {
        await using var context = new MainWindowTestContext();
        var text = "abcdefgh";
        var id = context.Session.Editor.AddSubtitle(MediaTime.Zero, new(4), text);
        context.Session.Editor.UpdateSubtitle(id, line => line with
        {
            Karaoke = [.. Enumerable.Range(0, text.Length).Select(index => new KaraokeSegment(index, 1,
                new(index, 50), new(index + 1, 50), SceneColor.White))]
        });
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var window = Assert.Single(context.Window.Layouts.FloatingWindows);
        window.Width = 460;
        window.Height = 900;
        var axis = UiTestActions.Find<KaraokeClipAxis>(window, "KaraokeAxis");
        axis.IsSnapEnabled = false;
        axis.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var origin = axis.TranslatePoint(default, window)!.Value;
        context.Session.Details.SelectClip(original.Subtitles[0].Karaoke[5].Id);
        Flush(window);
        var point = axis.TranslatePoint(axis.GeometryFor(original.Subtitles[0].Karaoke[5].Id).EndHandle.Center, window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseMove(point + new Vector(20, 0));
        window.UpdateLayout();
        Assert.Equal(text.Length, axis.DurationLabels.Count);
        Assert.Equal(origin, axis.TranslatePoint(default, window));
        Assert.Equal(88, axis.Bounds.Height);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Capture(window, "details-karaoke-duration-labels-dense.png");
        window.MouseUp(point + new Vector(20, 0), MouseButton.Left);
        Assert.Empty(axis.DurationLabels);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public void PersistentLabelsShowActualDurationsWithoutAGestureAndRefreshAfterLayoutAndContentChanges()
    {
        using var environment = new UiTestEnvironment();
        var line = DurationLine();
        var offset = new MediaTime(1, 13);
        var axis = new KaraokeClipAxis();
        axis.SetContent(line, offset, line.Karaoke[1].Id);
        var requests = 0;
        axis.RangeRequested += (_, _) => requests++;
        var container = new StackPanel
        {
            Children = { new Border { Height = 150 }, axis }
        };
        var window = new Window
        {
            Width = 460,
            Height = 300,
            Content = container
        };
        try
        {
            window.Show();
            Flush(window);
            Assert.False(axis.KeepDurationLabelsVisible);
            Assert.Empty(axis.DurationLabels);
            var origin = axis.TranslatePoint(default, window)!.Value;
            axis.KeepDurationLabelsVisible = true;
            Flush(window);
            AssertDurationLabels(axis, line, offset);
            Assert.Equal(origin, axis.TranslatePoint(default, window));
            Assert.Equal(88, axis.Bounds.Height);
            var previousCenter = axis.DurationLabels.Single(label => label.ClipId == line.Karaoke[^1].Id).Bounds.Center.X;
            window.Width = 300;
            Flush(window);
            AssertDurationLabels(axis, line, offset);
            Assert.True(axis.DurationLabels.Single(label => label.ClipId == line.Karaoke[^1].Id).Bounds.Center.X < previousCenter);
            var replacement = new SubtitleLine
            {
                Text = "xy",
                End = new(4),
                Karaoke =
                [
                    new(0, 1, new(1, 3), new(7, 6), SceneColor.White),
                    new(1, 1, new(3, 2), new(5, 2), SceneColor.White)
                ]
            };
            axis.SetContent(replacement, MediaTime.Zero, replacement.Karaoke[0].Id);
            Flush(window);
            AssertDurationLabels(axis, replacement, MediaTime.Zero);
            Assert.DoesNotContain(axis.DurationLabels, label => line.Karaoke.Any(clip => clip.Id == label.ClipId));
            axis.SetContent(null, MediaTime.Zero, null);
            Flush(window);
            Assert.Empty(axis.DurationLabels);
            axis.SetContent(replacement with { Karaoke = [] }, MediaTime.Zero, null);
            Flush(window);
            Assert.Empty(axis.DurationLabels);
            axis.SetContent(replacement, MediaTime.Zero, null);
            Flush(window);
            AssertDurationLabels(axis, replacement, MediaTime.Zero);
            axis.IsEnabled = false;
            Flush(window);
            AssertDurationLabels(axis, replacement, MediaTime.Zero);
            axis.IsEnabled = true;
            axis.IsVisible = false;
            Flush(window);
            Assert.Empty(axis.DurationLabels);
            Assert.True(axis.KeepDurationLabelsVisible);
            axis.IsVisible = true;
            Flush(window);
            AssertDurationLabels(axis, replacement, MediaTime.Zero);
            container.IsVisible = false;
            Flush(window);
            Assert.True(axis.IsVisible);
            Assert.False(axis.IsEffectivelyVisible);
            Assert.Empty(axis.DurationLabels);
            container.IsVisible = true;
            Flush(window);
            Assert.True(axis.IsEffectivelyVisible);
            AssertDurationLabels(axis, replacement, MediaTime.Zero);
            axis.KeepDurationLabelsVisible = false;
            Flush(window);
            Assert.Empty(axis.DurationLabels);
            axis.KeepDurationLabelsVisible = true;
            Flush(window);
            AssertDurationLabels(axis, replacement, MediaTime.Zero);
            Assert.Equal(0, requests);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void PersistentLabelsPreviewTheDraggedDurationAndReturnToActualDataAfterReleaseOrCancellation()
    {
        using var environment = new UiTestEnvironment();
        var line = DurationLine();
        var axis = new KaraokeClipAxis { IsSnapEnabled = false, KeepDurationLabelsVisible = true };
        axis.SelectionRequested += (_, e) => axis.SetContent(line, MediaTime.Zero, e.PrimaryClipId, e.SelectedClipIds);
        var requests = new List<KaraokeClipRangeEventArgs>();
        axis.RangeRequested += (_, e) => requests.Add(e);
        axis.SetContent(line, MediaTime.Zero, null);
        var window = new Window
        {
            Width = 460,
            Height = 300,
            Content = new StackPanel { Children = { new Border { Height = 150 }, axis } }
        };
        try
        {
            window.Show();
            Flush(window);
            AssertDurationLabels(axis, line, MediaTime.Zero);
            var pixels = (axis.Bounds.Width - 24) / 4;
            var clip = line.Karaoke[1];
            var point = axis.TranslatePoint(axis.GeometryFor(clip.Id).EndHandle.Center, window)!.Value;
            var moved = point + new Vector(20, 0);
            var delta = new MediaTime((long)Math.Round(20 / pixels * TimeSpan.TicksPerSecond), TimeSpan.TicksPerSecond);
            var expectedDuration = clip.End - clip.Start + delta;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(moved);
            Flush(window);
            Assert.Equal(line.Karaoke.Length, axis.DurationLabels.Count);
            Assert.Equal(expectedDuration, axis.DurationLabels.Single(label => label.ClipId == clip.Id).Duration);
            Assert.Equal(line.Karaoke[0].End - line.Karaoke[0].Start, axis.DurationLabels.Single(label => label.ClipId == line.Karaoke[0].Id).Duration);
            Assert.Equal(line.Karaoke[2].End - line.Karaoke[2].Start, axis.DurationLabels.Single(label => label.ClipId == line.Karaoke[2].Id).Duration);
            Assert.Empty(requests);
            axis.KeepDurationLabelsVisible = false;
            Flush(window);
            Assert.Equal(expectedDuration, axis.DurationLabels.Single(label => label.ClipId == clip.Id).Duration);
            Assert.Empty(requests);
            axis.KeepDurationLabelsVisible = true;
            window.MouseUp(moved, MouseButton.Left);
            Flush(window);
            var request = Assert.Single(requests);
            Assert.Equal(clip.Id, request.ClipId);
            Assert.Equal(expectedDuration, request.End - request.Start);
            AssertDurationLabels(axis, line, MediaTime.Zero);
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(moved);
            Flush(window);
            Assert.Equal(expectedDuration, axis.DurationLabels.Single(label => label.ClipId == clip.Id).Duration);
            axis.CancelGesture();
            Flush(window);
            Assert.Single(requests);
            AssertDurationLabels(axis, line, MediaTime.Zero);
            window.MouseUp(moved, MouseButton.Left);
            Assert.Single(requests);
            axis.KeepDurationLabelsVisible = false;
            Flush(window);
            Assert.Empty(axis.DurationLabels);
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(moved);
            Flush(window);
            Assert.Equal(line.Karaoke.Length, axis.DurationLabels.Count);
            Assert.Equal(expectedDuration, axis.DurationLabels.Single(label => label.ClipId == clip.Id).Duration);
            window.MouseUp(moved, MouseButton.Left);
            Flush(window);
            Assert.Empty(axis.DurationLabels);
            Assert.Equal(2, requests.Count);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("en-US")]
    [InlineData("zh-CN")]
    public async Task ActualTimeLabelsToolbarToggleControlsPresentationAndLocalizesWithoutAProjectTransaction(string language)
    {
        await using var context = new MainWindowTestContext();
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        var line = DurationLine();
        var id = context.Session.Editor.AddSubtitle(line.Start, line.End, line.Text);
        context.Session.Editor.UpdateSubtitle(id, value => value with { Karaoke = line.Karaoke });
        var original = context.Session.Editor.Snapshot;
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(id);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var window = Assert.Single(context.Window.Layouts.FloatingWindows);
        window.Width = 460;
        window.Height = 900;
        Flush(window);
        var axis = UiTestActions.Find<KaraokeClipAxis>(window, "KaraokeAxis");
        var toggle = UiTestActions.Find<ToolbarToggleButton>(window, "KaraokeTimeLabelsToggle");
        Assert.Same(UiTestActions.Find<Panel>(window, "SelectionStyleToolbar"), toggle.Parent);
        Assert.Equal(MaterialIconKind.ClockOutline, Assert.IsType<MaterialIcon>(toggle.Content).Kind);
        Assert.Equal(32, toggle.Bounds.Width);
        Assert.Equal(32, toggle.Bounds.Height);
        Assert.False(toggle.IsChecked);
        Assert.False(axis.KeepDurationLabelsVisible);
        Assert.Empty(axis.DurationLabels);
        UiTestActions.Click(window, "KaraokeTimeLabelsToggle");
        Flush(window);
        Assert.True(toggle.IsChecked);
        Assert.True(axis.KeepDurationLabelsVisible);
        AssertDurationLabels(axis, original.Subtitles[0], MediaTime.Zero);
        foreach (var selectedLanguage in new[] { language, language == "zh-CN" ? "en-US" : "zh-CN" })
        {
            context.Session.UpdatePreferences(context.Session.Preferences with { Language = selectedLanguage });
            Flush(window);
            Assert.Equal(Localization.Get("Workbench.KaraokeKeepTimeLabelsHint"), ToolTip.GetTip(toggle));
            Assert.Equal(Localization.Get("Workbench.KaraokeKeepTimeLabels"), AutomationProperties.GetName(toggle));
            Assert.True(toggle.IsChecked);
            Assert.True(axis.KeepDurationLabelsVisible);
            AssertDurationLabels(axis, original.Subtitles[0], MediaTime.Zero);
            Assert.Same(original, context.Session.Editor.Snapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        UiTestActions.Click(window, "KaraokeTimeLabelsToggle");
        Flush(window);
        Assert.False(toggle.IsChecked);
        Assert.False(axis.KeepDurationLabelsVisible);
        Assert.Empty(axis.DurationLabels);
        UiTestActions.Click(window, "KaraokeTimeLabelsToggle");
        Flush(window);
        AssertDurationLabels(axis, original.Subtitles[0], MediaTime.Zero);
        context.Session.Details.SelectClip(original.Subtitles[0].Karaoke[2].Id);
        Flush(window);
        AssertDurationLabels(axis, original.Subtitles[0], MediaTime.Zero);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.False(context.Session.Details.StyleDraft.IsDirty);
        Assert.False(context.Session.Details.HighlightDraft.IsDirty);
    }

    private static SubtitleLine DurationLine()
    {
        return new()
        {
            Text = "abc",
            End = new(4),
            Karaoke =
            [
                new(0, 1, new(1, 7), new(5, 6), SceneColor.White),
                new(1, 1, new(11, 12), new(17, 9), SceneColor.White),
                new(2, 1, new(19, 9), new(31, 11), SceneColor.White)
            ]
        };
    }

    private static void AssertDurationLabels(KaraokeClipAxis axis, SubtitleLine line, MediaTime offset)
    {
        Assert.Equal(line.Karaoke.Length, axis.DurationLabels.Count);
        foreach (var clip in line.Karaoke)
        {
            var label = Assert.Single(axis.DurationLabels, item => item.ClipId == clip.Id);
            var duration = clip.End - clip.Start;
            Assert.Equal(duration, label.Duration);
            Assert.Equal(((decimal)duration.Numerator / duration.Denominator).ToString("0.#######", CultureInfo.InvariantCulture) + " s", label.Text);
            var center = axis.GeometryFor(clip.Id).TimeBounds.Center.X;
            Assert.InRange(Math.Abs(label.Bounds.Center.X - center), 0, 0.001);
            Assert.True(label.Bounds.Width > 8);
        }
    }

    private static void Flush(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }

    private static double Seconds(MediaTime value) => (double)value.Numerator / value.Denominator;

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        if (!Path.IsPathFullyQualified(directory))
        {
            throw new InvalidOperationException("Headless capture directory must be an absolute isolated artifacts path.");
        }
        Directory.CreateDirectory(directory);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The headless Skia renderer did not produce a frame.");
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
