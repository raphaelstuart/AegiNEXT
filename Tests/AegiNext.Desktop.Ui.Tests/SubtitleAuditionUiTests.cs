using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controllers;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Audio;
using AegiNext.Media.Playback;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleAuditionUiTests
{
    [AvaloniaTheory]
    [InlineData(Key.Q, 1250, 2000)]
    [InlineData(Key.W, 3000, 3750)]
    [InlineData(Key.E, 2000, 2750)]
    [InlineData(Key.R, 2000, 3000)]
    public async Task ConfiguredMillisecondLengthControlsExactAudioSamplesWhileRPlaysTheWholeCue(Key key, int startMs, int endMs)
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var timeline = await PrepareAsync(context);
        context.Session.UpdatePreferences(current => current with { SubtitleAuditionMilliseconds = 750 });
        await FreezeVideoAsync(context);
        Assert.True(timeline.Focus());

        UiTestActions.Press(context.Window, key);

        await WaitForAudioStartAsync(source, output, new(startMs, 1000), 1);
        await DrainAuditionAsync(context, output);
        await AssertEndedAtAsync(context, new(endMs, 1000));
        Assert.Equal((endMs - startMs) * 48 * 2, output.Written.Length);
        Assert.Equal(startMs * 48, output.Written[0]);
        Assert.Equal(endMs * 48 - 1, output.Written[^1]);
        Assert.Null(context.Controller.Snapshot.Error);
        Assert.Null(context.Controller.Snapshot.AudioError);
    }

    [AvaloniaTheory]
    [InlineData(Key.Q, 4500, 5000)]
    [InlineData(Key.W, 6000, 6500)]
    [InlineData(Key.E, 5000, 5500)]
    [InlineData(Key.R, 5000, 6000)]
    public async Task LinkedAuditionUsesAbsoluteMediaTimeAndRelativeUiPositionWithNonzeroOrigin(Key key, int startMs, int endMs)
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output, new(3));
        var timeline = await PrepareAsync(context);
        await FreezeVideoAsync(context);
        Assert.True(timeline.Focus());

        UiTestActions.Press(context.Window, key);

        var start = new MediaTime(startMs, 1000);
        await WaitForAudioStartAsync(source, output, start, 1);
        await WaitForLinkedPositionAsync(context, start);
        await AdvanceAuditionAsync(context, output, start + new MediaTime(1, 10));
        await DrainAuditionAsync(context, output);
        await AssertEndedAtAsync(context, new(endMs, 1000));
        Assert.Equal((endMs - startMs) * 48 * 2, output.Written.Length);
        Assert.Equal(startMs * 48, output.Written[0]);
        Assert.Equal(endMs * 48 - 1, output.Written[^1]);
    }

    [AvaloniaFact]
    public async Task RapidQThenWReplacesThePreviousRangeAndPresentsOnlyTheLatestPosition()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var timeline = await PrepareAsync(context);
        await FreezeVideoAsync(context);
        Assert.True(timeline.Focus());

        UiTestActions.Press(context.Window, Key.Q);
        UiTestActions.Press(context.Window, Key.W);

        await EventuallyAsync(() => source.LastSeek == new MediaTime(3) && !output.Paused);
        await WaitForLinkedPositionAsync(context, new(3));
        await AdvanceAuditionAsync(context, output, new(31, 10));
        await DrainAuditionAsync(context, output);
        await AssertEndedAtAsync(context, new(7, 2));
        Assert.Equal(24000 * 2, output.Written.Length);
        Assert.Equal(144000, output.Written[0]);
        Assert.Equal(168000 - 1, output.Written[^1]);
        Assert.Null(context.Controller.Snapshot.Error);
        Assert.Null(context.Controller.Snapshot.AudioError);
    }

    [AvaloniaTheory]
    [InlineData("Space")]
    [InlineData("PlayButton")]
    public async Task TransportResumesNormalPlaybackFromTheAuditionEndInsteadOfTheMediaStart(string input)
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var timeline = await PrepareAsync(context);
        await FreezeVideoAsync(context);
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.R);
        await WaitForAudioStartAsync(source, output, new(2), 1);
        await DrainAuditionAsync(context, output);
        await AssertEndedAtAsync(context, new(3));

        if (input == "Space")
        {
            UiTestActions.Press(context.Window, Key.Space);
        }
        else
        {
            UiTestActions.Click(context.Window, "PlayButton");
        }

        await WaitForAudioStartAsync(source, output, new(3), 2);
        await WaitForLinkedPositionAsync(context, new(3));
        Assert.False(context.Controller.IsRangePlaybackActive);
        await AdvanceAuditionAsync(context, output, new(31, 10));
        Assert.True(context.ViewModel.Preview.IsPlaying);
        Assert.Equal(Localization.Get("Preview.Pause"), context.ViewModel.Preview.PlayLabel);
    }

    [AvaloniaFact]
    public async Task AuditionAtTheActualMediaEndRestartsNormalPlaybackAtTheMediaOrigin()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        await PrepareAsync(context);
        var cue = context.Session.SelectedCue!;
        context.Session.Editor.SetSubtitleTiming(cue.Id, new(39, 2), new(20), AegiNext.Core.Editing.TimelineEditMode.CROP);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.R);
        await WaitForAudioStartAsync(source, output, new(39, 2), 1);
        await DrainAuditionAsync(context, output);
        await AssertEndedAtAsync(context, new(20));
        Assert.True(timeline.Focus());

        UiTestActions.Press(context.Window, Key.Space);

        await WaitForAudioStartAsync(source, output, MediaTime.Zero, 2);
        await WaitForLinkedPositionAsync(context, MediaTime.Zero);
        Assert.False(context.Controller.IsRangePlaybackActive);
    }

    [AvaloniaTheory]
    [InlineData(Key.Q, 1500, 2000)]
    [InlineData(Key.W, 3000, 3500)]
    [InlineData(Key.E, 2000, 2500)]
    [InlineData(Key.R, 2000, 3000)]
    public async Task TimelineQwerPlaysExactAudioSamplesAndLinksPresentedVideoAndPlaybackPosition(Key key, int startMs, int endMs)
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using (var context = CreateContext(source, output))
        {
            var timeline = await PrepareAsync(context);
            var frozen = await FreezeVideoAsync(context);
            Assert.True(timeline.Focus());
            Assert.True(context.Controller.Snapshot.AudioAvailable);
            var starts = output.PlaybackStartCount;

            UiTestActions.Press(context.Window, key);
            await WaitForAudioStartAsync(source, output, new(startMs, 1000), starts + 1);

            var start = new MediaTime(startMs, 1000);
            var end = new MediaTime(endMs, 1000);
            await WaitForLinkedPositionAsync(context, start);
            Assert.True(context.ViewModel.Preview.IsPlaying);
            Assert.False(context.Controller.Snapshot.AudioAuditionActive);
            Assert.Equal(Localization.Get("Preview.Pause"), context.ViewModel.Preview.PlayLabel);
            Assert.NotEqual(frozen.PresentedGeneration, context.Controller.Snapshot.PresentedGeneration);
            await AdvanceAuditionAsync(context, output, start + new MediaTime(1, 10));
            await DrainAuditionAsync(context, output);
            await AssertEndedAtAsync(context, end);
            var written = output.Written;
            Assert.Equal((endMs - startMs) * 48 * 2, written.Length);
            Assert.Equal(startMs * 48, written[0]);
            Assert.Equal(endMs * 48 - 1, written[^1]);
            Assert.True(context.Controller.Snapshot.PresentedFrameTime >= start);
            Assert.True(context.Controller.Snapshot.PresentedFrameTime < end);
            Assert.False(context.Controller.Snapshot.AudioAuditionActive);
            Assert.Equal(Localization.Get("Preview.Play"), context.ViewModel.Preview.PlayLabel);
            Assert.Null(context.Controller.Snapshot.AudioError);
            Assert.Null(context.Controller.Snapshot.Error);
        }
        Assert.Equal(1, source.DisposeCount);
        Assert.Equal(1, output.DisposeCount);
    }

    [AvaloniaTheory]
    [InlineData("Space")]
    [InlineData("PlayButton")]
    public async Task TransportPausesLinkedAuditionAtTheCurrentPositionAndFrame(string input)
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var timeline = await PrepareAsync(context);
        await FreezeVideoAsync(context);
        Assert.True(timeline.Focus());
        UiTestActions.Press(context.Window, Key.R);
        await WaitForAudioStartAsync(source, output, new(2), 1);
        await WaitForLinkedPositionAsync(context, new(2));
        await AdvanceAuditionAsync(context, output, new(21, 10));
        var position = context.Controller.Snapshot.Position;
        Assert.False(context.Controller.Snapshot.AudioAuditionActive);
        Assert.Equal(Localization.Get("Preview.Pause"), context.ViewModel.Preview.PlayLabel);

        if (input == "Space")
        {
            UiTestActions.Press(context.Window, Key.Space);
        }
        else
        {
            UiTestActions.Click(context.Window, "PlayButton");
        }
        await EventuallyAsync(() => output.Paused && !context.Controller.IsRangePlaybackActive &&
            !context.ViewModel.Preview.IsPlaying && context.Controller.Snapshot.PresentedFrameTime == position);

        Assert.False(context.Controller.Snapshot.AudioAuditionActive);
        Assert.Equal(Localization.Get("Preview.Play"), context.ViewModel.Preview.PlayLabel);
        Assert.Equal(1, output.PlaybackStartCount);
        var paused = context.Controller.Snapshot;
        Assert.Equal(position, paused.Position);
        AssertFrozen(context, paused);
        output.ConsumeAvailable();
        context.Clock.Advance(TimeSpan.FromSeconds(1));
        await Task.Delay(20, TestContext.Current.CancellationToken);
        AssertFrozen(context, paused);
        Assert.True(context.Window.GetCommand(WorkbenchCommand.PLAY_PAUSE).CanExecute(null));
    }

    [AvaloniaFact]
    public async Task MultiSelectionAuditionsThePrimarySubtitleRatherThanTheCombinedSelection()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var timeline = await PrepareAsync(context, true);
        Assert.Equal(2, context.ViewModel.Timeline.SelectedLayerIds.Count);
        Assert.Equal(new MediaTime(4), context.Session.SelectedCue!.Start);
        await FreezeVideoAsync(context);
        Assert.True(timeline.Focus());

        UiTestActions.Press(context.Window, Key.R);
        await WaitForAudioStartAsync(source, output, new(4), 1);
        await DrainAuditionAsync(context, output);

        Assert.Equal(48000 * 2, output.Written.Length);
        Assert.Equal(4 * 48000, output.Written[0]);
        Assert.Equal(5 * 48000 - 1, output.Written[^1]);
        Assert.Equal(2, context.ViewModel.Timeline.SelectedLayerIds.Count);
        await AssertEndedAtAsync(context, new(5));
    }

    [AvaloniaFact]
    public async Task RepeatedKeyDownStartsOnceAndKeyUpStaysConsumedAfterTheBindingIsCleared()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var timeline = await PrepareAsync(context);
        await FreezeVideoAsync(context);
        Assert.True(timeline.Focus());
        var downHandled = new List<bool>();
        var upHandled = new List<bool>();
        void OnDown(object? sender, KeyEventArgs args) => downHandled.Add(args.Handled);
        void OnUp(object? sender, KeyEventArgs args) => upHandled.Add(args.Handled);
        context.Window.AddHandler(InputElement.KeyDownEvent, OnDown, RoutingStrategies.Bubble, true);
        context.Window.AddHandler(InputElement.KeyUpEvent, OnUp, RoutingStrategies.Bubble, true);
        try
        {
            var seeks = source.SeekCount;
            context.Window.KeyPress(Key.Q, RawInputModifiers.None, PhysicalKey.None, null);
            context.Window.KeyPress(Key.Q, RawInputModifiers.None, PhysicalKey.None, null);
            await WaitForAudioStartAsync(source, output, new(3, 2), 1);
            context.Window.KeyPress(Key.Q, RawInputModifiers.None, PhysicalKey.None, null);

            Assert.Equal(seeks + 1, source.SeekCount);
            Assert.Equal(1, output.PlaybackStartCount);
            Assert.Equal(3, downHandled.Count);
            Assert.All(downHandled, handled => Assert.True(handled));
            var bindings = context.Session.Preferences.ShortcutBindings.Select(binding =>
                binding.Command == WorkbenchCommand.AUDITION_BEFORE_SUBTITLE ? binding with { Gesture = "" } : binding).ToImmutableArray();
            context.Session.UpdatePreferences(context.Session.Preferences with { ShortcutBindings = bindings });
            context.Window.KeyRelease(Key.Q, RawInputModifiers.None, PhysicalKey.None, null);

            Assert.True(Assert.Single(upHandled));
            await DrainAuditionAsync(context, output);
            UiTestActions.Press(context.Window, Key.Q);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, output.PlaybackStartCount);
            Assert.Equal(seeks + 1, source.SeekCount);
            await AssertEndedAtAsync(context, new(2));
        }
        finally
        {
            context.Window.RemoveHandler(InputElement.KeyDownEvent, OnDown);
            context.Window.RemoveHandler(InputElement.KeyUpEvent, OnUp);
        }
    }

    [AvaloniaFact]
    public async Task RebindingAndClearingEveryAuditionCommandUpdatesRealTimelineInputImmediately()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        var timeline = await PrepareAsync(context);
        await FreezeVideoAsync(context);
        var bindings = context.Session.Preferences.ShortcutBindings.Select(binding => binding.Command switch
        {
            WorkbenchCommand.AUDITION_BEFORE_SUBTITLE => binding with { Gesture = "F2" },
            WorkbenchCommand.AUDITION_AFTER_SUBTITLE => binding with { Gesture = "F3" },
            WorkbenchCommand.AUDITION_SUBTITLE_BEGIN => binding with { Gesture = "F4" },
            WorkbenchCommand.AUDITION_SUBTITLE => binding with { Gesture = "F5" },
            _ => binding
        }).ToImmutableArray();
        context.Session.UpdatePreferences(context.Session.Preferences with { ShortcutBindings = bindings });
        Assert.True(timeline.Focus());

        foreach (var key in new[] { Key.Q, Key.W, Key.E, Key.R })
        {
            UiTestActions.Press(context.Window, key);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, output.PlaybackStartCount);
        foreach (var (key, start) in new[] { (Key.F2, new MediaTime(3, 2)), (Key.F3, new MediaTime(3)),
            (Key.F4, new MediaTime(2)), (Key.F5, new MediaTime(2)) })
        {
            var starts = output.PlaybackStartCount;
            UiTestActions.Press(context.Window, key);
            await WaitForAudioStartAsync(source, output, start, starts + 1);
            await DrainAuditionAsync(context, output);
        }
        Assert.Equal(4, output.PlaybackStartCount);
        bindings = bindings.Select(binding => IsAuditionCommand(binding.Command) ? binding with { Gesture = "" } : binding).ToImmutableArray();
        context.Session.UpdatePreferences(context.Session.Preferences with { ShortcutBindings = bindings });
        foreach (var key in new[] { Key.F2, Key.F3, Key.F4, Key.F5, Key.Q, Key.W, Key.E, Key.R })
        {
            UiTestActions.Press(context.Window, key);
        }
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(4, output.PlaybackStartCount);
        Assert.False(context.Controller.IsRangePlaybackActive);
        await AssertEndedAtAsync(context, new(3));
    }

    [AvaloniaFact]
    public async Task SubtitleTextAndOtherPanelFocusKeepQwerLocalWithoutStartingAudio()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        await PrepareAsync(context);
        var cue = context.Session.SelectedCue!;
        var frozen = await FreezeVideoAsync(context);
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        var input = list.GetVisualDescendants().OfType<TextBox>().Single(control =>
            control.DataContext is SubtitleRow row && row.Id == cue.Id && Grid.GetColumn(control) == 4);
        Assert.True(input.Focus());
        input.CaretIndex = input.Text!.Length;
        var originalText = input.Text;
        var seeks = source.SeekCount;
        foreach (var (key, text) in new[] { (Key.Q, "q"), (Key.W, "w"), (Key.E, "e"), (Key.R, "r") })
        {
            context.Window.KeyPress(key, RawInputModifiers.None, PhysicalKey.None, null);
            context.Window.KeyTextInput(text);
            context.Window.KeyRelease(key, RawInputModifiers.None, PhysicalKey.None, null);
        }
        Assert.Equal(originalText + "qwer", input.Text);
        Assert.Equal(0, output.PlaybackStartCount);
        Assert.Equal(seeks, source.SeekCount);
        context.ViewModel.Subtitles.Rows.Single(row => row.Id == cue.Id).Accept(cue);
        Assert.True(UiTestActions.Find<Button>(context.Window, "AddCueButton").Focus());
        foreach (var key in new[] { Key.Q, Key.W, Key.E, Key.R })
        {
            UiTestActions.Press(context.Window, key);
        }
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(0, output.PlaybackStartCount);
        Assert.Equal(seeks, source.SeekCount);
        Assert.False(context.Controller.IsRangePlaybackActive);
        AssertFrozen(context, frozen);
    }

    [AvaloniaTheory]
    [InlineData(Key.Q, WorkbenchCommand.AUDITION_BEFORE_SUBTITLE)]
    [InlineData(Key.W, WorkbenchCommand.AUDITION_AFTER_SUBTITLE)]
    [InlineData(Key.E, WorkbenchCommand.AUDITION_SUBTITLE_BEGIN)]
    [InlineData(Key.R, WorkbenchCommand.AUDITION_SUBTITLE)]
    public async Task MediaWithoutAudioDisablesAuditionEvenWithTimelineFocus(Key key, WorkbenchCommand command)
    {
        await using var context = new MainWindowTestContext();
        var timeline = await PrepareAsync(context);
        var frozen = await FreezeVideoAsync(context, false);
        Assert.True(timeline.Focus());
        Assert.False(context.Controller.Snapshot.AudioAvailable);
        Assert.False(context.Session.CanAuditionSubtitle);
        Assert.False(context.Window.GetCommand(command).CanExecute(null));

        UiTestActions.Press(context.Window, key);
        Dispatcher.UIThread.RunJobs();

        Assert.False(context.Controller.IsRangePlaybackActive);
        Assert.Null(context.Controller.Snapshot.Error);
        AssertFrozen(context, frozen);
    }

    [AvaloniaFact]
    public async Task FloatingTimelineUsesItsRegisteredWindowToAuditionTheSameSelection()
    {
        var source = new UiAuditionAudioSource();
        var output = new UiAuditionAudioOutput();
        await using var context = CreateContext(source, output);
        await PrepareAsync(context);
        await FreezeVideoAsync(context);
        context.Window.Layouts.Float("timeline");
        Dispatcher.UIThread.RunJobs();
        var floating = Assert.Single(context.Window.Layouts.FloatingWindows);
        try
        {
            Flush(floating);
            var timeline = UiTestActions.Find<SubtitleTimelineControl>(floating, "Timeline");
            Assert.Contains(floating, context.WindowRegistry.Windows);
            Assert.True(timeline.Focus());

            UiTestActions.Press(floating, Key.Q);
            await WaitForAudioStartAsync(source, output, new(3, 2), 1);
            await DrainAuditionAsync(context, output);

            Assert.Equal(24000 * 2, output.Written.Length);
            Assert.Equal(72000, output.Written[0]);
            Assert.Equal(96000 - 1, output.Written[^1]);
            await AssertEndedAtAsync(context, new(2));
        }
        finally
        {
            floating.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.False(floating.IsVisible);
            Assert.DoesNotContain(floating, context.WindowRegistry.Windows);
        }
    }

    private static MainWindowTestContext CreateContext(UiAuditionAudioSource source, UiAuditionAudioOutput output,
        MediaTime? mediaStart = null)
    {
        var origin = (mediaStart ?? MediaTime.Zero).ToTimestamp(new(1, 1000), MediaTimeRounding.FLOOR).Value;
        return new((_, _, position, _) => Task.FromResult(new AudioPlaybackSession(source, output, position)),
            () => new(1, Enumerable.Range(0, 201).Select(index => origin + index * 100).ToArray()), mediaStart);
    }

    private static async Task<SubtitleTimelineControl> PrepareAsync(MainWindowTestContext context, bool multiple = false)
    {
        await context.OpenMediaAsync();
        var first = new SubtitleLine { Text = "First subtitle", Start = new(2), End = new(3) };
        var second = new SubtitleLine { Text = "Primary subtitle", Start = new(4), End = new(5) };
        context.Session.Editor.Reset(context.Session.DocumentSnapshot with
        {
            Subtitles = [first, second], Layers = [Layer(first), Layer(second)]
        });
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        context.ViewModel.Timeline.IsSnapEnabled = false;
        context.ViewModel.Timeline.IsStepEnabled = false;
        timeline.PixelsPerSecond = 60;
        timeline.ViewStart = 0;
        Flush(context.Window);
        ClickClip(context, timeline, first.Id);
        if (multiple)
        {
            ClickClip(context, timeline, second.Id, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control);
        }
        Assert.Equal(multiple ? second.Id : first.Id, context.Session.SelectedCue!.Id);
        Assert.Equal(multiple ? 2 : 1, context.ViewModel.Timeline.SelectedLayerIds.Count);
        return timeline;
    }

    private static ProjectLayer Layer(SubtitleLine cue) => new()
    {
        Id = cue.Id, Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End
    };

    private static void ClickClip(MainWindowTestContext context, SubtitleTimelineControl timeline, Guid id,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var point = timeline.TranslatePoint(timeline.GetClipRectangle(id)!.Value.Center, context.Window)!.Value;
        Assert.Same(timeline, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left, modifiers);
        context.Window.MouseUp(point, MouseButton.Left, modifiers);
        Flush(context.Window);
        Assert.False(timeline.HasActiveDrag);
        Assert.True(timeline.IsFocused);
    }

    private static async Task<VideoPreviewSnapshot> FreezeVideoAsync(MainWindowTestContext context, bool expectAudio = true)
    {
        await context.Session.SeekProjectTimeAsync(new(10));
        await EventuallyAsync(() => context.Controller.Snapshot.PresentedFrameTime == new MediaTime(10) +
            (context.Controller.Snapshot.Start ?? MediaTime.Zero) &&
            (!expectAudio || context.Session.CanAuditionSubtitle));
        return context.Controller.Snapshot;
    }

    private static Task WaitForAudioStartAsync(UiAuditionAudioSource source, UiAuditionAudioOutput output,
        MediaTime start, int count) => EventuallyAsync(() => source.LastSeek == start && !output.Paused && output.PlaybackStartCount == count);

    private static async Task DrainAuditionAsync(MainWindowTestContext context, UiAuditionAudioOutput output)
    {
        await EventuallyAsync(() =>
        {
            output.ConsumeAvailable();
            context.Clock.Advance(TimeSpan.FromMilliseconds(5));
            return !context.Controller.IsRangePlaybackActive && output.Paused;
        });
    }

    private static Task WaitForLinkedPositionAsync(MainWindowTestContext context, MediaTime position)
    {
        var relative = position - (context.Controller.Snapshot.Start ?? MediaTime.Zero);
        return EventuallyAsync(() =>
        {
            context.Clock.Advance(TimeSpan.FromMilliseconds(5));
            return context.Controller.Snapshot.PresentedFrameTime == position &&
                context.Controller.Snapshot.Position == position && context.ViewModel.Timeline.Position == relative &&
                UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline").Position == relative &&
                context.ViewModel.Preview.Position == (double)relative.Numerator / relative.Denominator &&
                UiTestActions.Find<Slider>(context.Window, "PositionSlider").Value == context.ViewModel.Preview.Position;
        });
    }

    private static async Task AdvanceAuditionAsync(MainWindowTestContext context, UiAuditionAudioOutput output, MediaTime position)
    {
        await EventuallyAsync(() => output.QueuedFrames >= 4800);
        output.Consume(4800);
        await WaitForLinkedPositionAsync(context, position);
        Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
    }

    private static async Task AssertEndedAtAsync(MainWindowTestContext context, MediaTime end)
    {
        var relative = end - (context.Controller.Snapshot.Start ?? MediaTime.Zero);
        await EventuallyAsync(() => !context.ViewModel.Preview.IsPlaying &&
            context.ViewModel.Timeline.Position == relative &&
            context.ViewModel.Preview.Position == (double)relative.Numerator / relative.Denominator);
        Assert.Equal(VideoPlaybackState.ENDED, context.Controller.Snapshot.State);
        Assert.Equal(end, context.Controller.Snapshot.Position);
        Assert.False(context.Controller.IsRangePlaybackActive);
    }

    private static void AssertFrozen(MainWindowTestContext context, VideoPreviewSnapshot frozen)
    {
        var snapshot = context.Controller.Snapshot;
        Assert.Equal(VideoPlaybackState.PAUSED, snapshot.State);
        Assert.Equal(frozen.Position, snapshot.Position);
        Assert.Equal(frozen.PresentedFrameTime, snapshot.PresentedFrameTime);
        Assert.Equal(frozen.PresentedAtPosition, snapshot.PresentedAtPosition);
        Assert.Equal(frozen.PresentedGeneration, snapshot.PresentedGeneration);
    }

    private static bool IsAuditionCommand(WorkbenchCommand command) => command is WorkbenchCommand.AUDITION_BEFORE_SUBTITLE or
        WorkbenchCommand.AUDITION_AFTER_SUBTITLE or WorkbenchCommand.AUDITION_SUBTITLE_BEGIN or WorkbenchCommand.AUDITION_SUBTITLE;

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task EventuallyAsync(Func<bool> predicate)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!predicate())
        {
            Assert.True(DateTime.UtcNow < deadline, "Audio audition did not reach the expected UI state.");
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(5, TestContext.Current.CancellationToken);
        }
    }
}
