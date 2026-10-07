using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Playback;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ClipBoundaryShortcutUiTests
{
    [AvaloniaTheory]
    [InlineData(Key.Q, false, false)]
    [InlineData(Key.W, false, false)]
    [InlineData(Key.Q, true, false)]
    [InlineData(Key.W, true, false)]
    [InlineData(Key.Q, false, true)]
    [InlineData(Key.W, false, true)]
    public async Task ShiftKeysSeekPrimaryClipExactlyAndPreservePlaybackAndSelection(Key key, bool playing, bool shape)
    {
        await using var context = CreateContext();
        var timeline = await PrepareAsync(context, shape);
        await context.Session.SeekProjectTimeAsync(new(10));
        if (playing)
        {
            await context.Controller.PlayAsync();
        }
        var document = context.Session.DocumentSnapshot;
        var selection = context.ViewModel.Timeline.SelectedLayerIds.ToArray();
        var primary = context.Session.SelectedLayer!;
        var target = key == Key.Q ? primary.Start : primary.End;
        timeline.Focus();

        Press(context.Window, key);

        await WaitForPositionAsync(context, target);
        Assert.Equal(target + new MediaTime(3), context.Controller.Snapshot.PresentedAtPosition);
        Assert.Equal((key == Key.Q ? new MediaTime(25, 2) : new MediaTime(68, 5)) + new MediaTime(3),
            context.Controller.Snapshot.PresentedFrameTime);
        Assert.Equal(playing ? VideoPlaybackState.PLAYING : VideoPlaybackState.PAUSED, context.Controller.Snapshot.State);
        Assert.Equal(selection, context.ViewModel.Timeline.SelectedLayerIds);
        Assert.Equal(primary.Id, context.Session.SelectedLayerId);
        Assert.Same(timeline, context.Window.FocusManager!.GetFocusedElement());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.True(context.ViewModel.Timeline.IsPlaybackFollowEnabled);
    }

    [AvaloniaTheory]
    [InlineData(Key.Q)]
    [InlineData(Key.W)]
    public async Task PausedSeekBringsOffscreenBoundaryIntoViewWithoutChangingZoom(Key key)
    {
        await using var context = CreateContext();
        var timeline = await PrepareAsync(context);
        context.ViewModel.Timeline.PixelsPerSecond = 200;
        context.ViewModel.Timeline.ViewStart = 0;
        context.ViewModel.Timeline.SuspendPlaybackFollow();
        Flush(context.Window);
        var scale = timeline.PixelsPerSecond;
        var target = key == Key.Q ? context.Session.SelectedLayer!.Start : context.Session.SelectedLayer!.End;
        timeline.Focus();

        Press(context.Window, key);

        await WaitForPositionAsync(context, target);
        Flush(context.Window);
        var seconds = (double)target.Numerator / target.Denominator;
        Assert.InRange(seconds, timeline.ViewStart, timeline.ViewStart + timeline.Viewport.VisibleDuration);
        Assert.Equal(scale, timeline.PixelsPerSecond);
        Assert.Equal(VideoPlaybackState.PAUSED, context.Controller.Snapshot.State);
        Assert.True(context.ViewModel.Timeline.IsPlaybackFollowEnabled);
    }

    [AvaloniaTheory]
    [InlineData(Key.Q, -1000, 1000, 0)]
    [InlineData(Key.W, 19000, 21000, 20000)]
    public async Task BoundariesOutsideMediaClampToPlayableRange(Key key, int startMs, int endMs, int expectedMs)
    {
        await using var context = CreateContext();
        var timeline = await PrepareAsync(context);
        var document = context.Session.DocumentSnapshot;
        var primary = document.Subtitles[1];
        context.Session.Editor.Reset(document with
        {
            Subtitles = document.Subtitles.SetItem(1, primary with { Start = new(startMs, 1000), End = new(endMs, 1000) }),
            Layers = document.Layers.SetItem(1, document.Layers[1] with { Start = new(startMs, 1000), End = new(endMs, 1000) })
        });
        timeline.Focus();

        Press(context.Window, key);

        await WaitForPositionAsync(context, new(expectedMs, 1000));
        Assert.Null(context.Session.LastError);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(Key.Q)]
    [InlineData(Key.W)]
    public async Task ShiftKeysKeepUppercaseSubtitleInputLocalWithoutSeeking(Key key)
    {
        await using var context = CreateContext();
        await PrepareAsync(context);
        await context.Session.SeekProjectTimeAsync(new(10));
        var cue = context.Session.SelectedCue!;
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        var input = list.GetVisualDescendants().OfType<TextBox>().Single(control =>
            control.DataContext is SubtitleRow row && row.Id == cue.Id && Grid.GetColumn(control) == 4);
        Assert.True(input.Focus());
        input.CaretIndex = input.Text!.Length;
        var original = input.Text;
        var document = context.Session.DocumentSnapshot;
        var before = context.Controller.Snapshot;

        context.Window.KeyPress(key, RawInputModifiers.Shift, PhysicalKey.None, null);
        context.Window.KeyTextInput(key.ToString());
        context.Window.KeyRelease(key, RawInputModifiers.Shift, PhysicalKey.None, null);
        Flush(context.Window);

        Assert.Equal(original + key, input.Text);
        Assert.Same(input, context.Window.FocusManager!.GetFocusedElement());
        Assert.Equal(before.Position, context.Controller.Snapshot.Position);
        Assert.Equal(before.PresentedGeneration, context.Controller.Snapshot.PresentedGeneration);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        context.ViewModel.Subtitles.Rows.Single(row => row.Id == cue.Id).Accept(cue);
    }

    [AvaloniaTheory]
    [InlineData(Key.Q)]
    [InlineData(Key.W)]
    public async Task WithoutSelectionShortcutDoesNotSeekOrCreateAClip(Key key)
    {
        await using var context = CreateContext();
        await context.OpenMediaAsync();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var document = context.Session.DocumentSnapshot;
        var before = context.Controller.Snapshot;
        var canUndo = context.Session.Editor.CanUndo;
        var router = new ShortcutRouter(context.Session.Preferences.ShortcutBindings);
        Assert.True(router.TryResolve(key, KeyModifiers.Shift, false, out var command));
        Assert.False(context.Session.CanExecuteCommand(command));
        timeline.Focus();

        Press(context.Window, key);
        Flush(context.Window);

        Assert.Equal(before.Position, context.Controller.Snapshot.Position);
        Assert.Equal(before.PresentedGeneration, context.Controller.Snapshot.PresentedGeneration);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.Empty(document.Subtitles);
        Assert.Equal(canUndo, context.Session.Editor.CanUndo);
    }

    private static MainWindowTestContext CreateContext()
    {
        return new(videoSourceFactory: () => new(1, Enumerable.Range(0, 201).Select(index => 3000L + index * 100).ToArray()),
            mediaStart: new(3));
    }

    private static async Task<SubtitleTimelineControl> PrepareAsync(MainWindowTestContext context, bool shape = false)
    {
        await context.OpenMediaAsync();
        var first = new SubtitleLine { Text = "First", Start = new(2), End = new(3) };
        var second = new SubtitleLine { Text = "Primary", Start = new(12501, 1000), End = new(13601, 1000) };
        var secondLayer = Layer(second);
        context.Session.Editor.Reset(context.Session.DocumentSnapshot with
        {
            Subtitles = shape ? [first] : [first, second],
            Layers = [Layer(first), shape ? secondLayer with
            {
                Kind = LayerKind.SHAPE, SubtitleId = null, Shape = new(ShapeKind.RECTANGLE, 100, 100)
            } : secondLayer]
        });
        context.Session.SelectLayer(secondLayer.Id, [first.Id, secondLayer.Id]);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Flush(context.Window);
        Assert.Equal(2, context.ViewModel.Timeline.SelectedLayerIds.Count);
        return timeline;
    }

    private static ProjectLayer Layer(SubtitleLine cue)
    {
        return new() { Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End };
    }

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.Shift, PhysicalKey.None, null);
        window.KeyRelease(key, RawInputModifiers.Shift, PhysicalKey.None, null);
    }

    private static async Task WaitForPositionAsync(MainWindowTestContext context, MediaTime target)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (context.Session.ProjectPosition != target || context.Controller.Snapshot.PresentedAtPosition != target + new MediaTime(3))
        {
            Assert.True(DateTime.UtcNow < deadline, "Clip boundary shortcut did not reach the expected position.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
