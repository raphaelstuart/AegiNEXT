using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class NativeKaraokeDetailsUiTests
{
    [AvaloniaFact]
    public async Task FirstEnableClickGeneratesGraphemesAndOffOnRestoresExactTimingIdsAndStyles()
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        var line = document.Subtitles[0] with
        {
            KaraokeStyleSpans = [new(1, 2, new() { Fill = new(0, 1, 0), StrokeWidth = 5 }, new() { StrokeWidth = 3 })]
        };
        var original = document with { Subtitles = [line] };
        context.Session.Editor.Reset(original);
        context.Session.SelectCue(line.Id);
        var host = await Open(context);
        var toggle = UiTestActions.Find<ToolbarToggleButton>(host, "EnableKaraokeToggle");
        Assert.True(toggle.IsEffectivelyEnabled);
        UiTestActions.Click(host, "EnableKaraokeToggle");
        Flush(host);
        var enabled = context.Session.Editor.Snapshot;
        var groups = enabled.Subtitles[0].Karaoke;
        Assert.Equal([1, 2, 1], groups.Select(clip => clip.Utf16Length));
        Assert.Equal(MediaTime.Zero, groups[0].Start);
        Assert.Equal(new MediaTime(4, 3), groups[0].End);
        Assert.Equal(new MediaTime(4), groups[^1].End);
        Assert.Equal(line.KaraokeStyleSpans, enabled.Subtitles[0].KaraokeStyleSpans);
        Capture(host, "native-first-enable.png");
        UiTestActions.Click(host, "EnableKaraokeToggle");
        Flush(host);
        var disabled = context.Session.Editor.Snapshot;
        Assert.Empty(disabled.Subtitles[0].Karaoke);
        Assert.Equal(groups.ToArray(), disabled.Subtitles[0].InactiveKaraoke.ToArray());
        UiTestActions.Click(host, "EnableKaraokeToggle");
        Flush(host);
        var restored = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.Equal(groups.ToArray(), restored.Karaoke.ToArray());
        Assert.Equal(line.KaraokeStyleSpans, restored.KaraokeStyleSpans);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(disabled, context.Session.Editor.Snapshot);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(enabled, context.Session.Editor.Snapshot);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(RawInputModifiers.Control)]
    [InlineData(RawInputModifiers.Meta)]
    public async Task RealAxisMultiSelectionUsesOneMergeButtonAndOneUndo(RawInputModifiers modifier)
    {
        await using var context = new MainWindowTestContext();
        var original = PrepareMultiple(context);
        var line = original.Subtitles[0];
        var host = await Open(context);
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        foreach (var group in line.Karaoke)
        {
            ClickAxis(host, axis, group.Id, modifier);
        }
        Assert.Equal(line.Karaoke.Select(clip => clip.Id), axis.SelectedClipIds);
        Assert.Equal(line.Karaoke.Select(clip => clip.Id), context.Session.Details.SelectedClipIds);
        Assert.DoesNotContain(host.GetVisualDescendants().OfType<Control>(), control =>
            control.Name is "MergePreviousKaraokeGroupButton" or "MergeNextKaraokeGroupButton");
        Assert.True(UiTestActions.Find<Button>(host, "MergeSelectedKaraokeGroupsButton").IsEffectivelyEnabled);
        Capture(host, "native-multiple-merge.png");
        UiTestActions.Click(host, "MergeSelectedKaraokeGroupsButton");
        Flush(host);
        var merged = Assert.Single(context.Session.Editor.Snapshot.Subtitles[0].Karaoke);
        Assert.Equal(line.Karaoke[0].Id, merged.Id);
        Assert.Equal(0, merged.Utf16Start);
        Assert.Equal(4, merged.Utf16Length);
        Assert.Equal(line.Karaoke[0].Start, merged.Start);
        Assert.Equal(line.Karaoke[^1].End, merged.End);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task OverlappingGroupsExposeAllHandlesAndExplainWhyMultiMergeIsDisabled()
    {
        await using var context = new MainWindowTestContext();
        var original = PrepareMultiple(context, true);
        var line = original.Subtitles[0];
        var host = await Open(context);
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        foreach (var group in line.Karaoke)
        {
            ClickAxis(host, axis, group.Id, RawInputModifiers.Control);
        }
        var merge = UiTestActions.Find<Button>(host, "MergeSelectedKaraokeGroupsButton");
        Assert.False(merge.IsEffectivelyEnabled);
        Assert.True(ToolTip.GetShowOnDisabled(merge));
        Assert.Equal(Localization.Get("Workbench.MergeKaraokeSelectionTimeAdjacent"), ToolTip.GetTip(merge));
        var geometries = line.Karaoke.Select(clip => axis.GeometryFor(clip.Id)).ToArray();
        Assert.Equal(3, geometries.Select(geometry => geometry.Body.Top).Distinct().Count());
        Assert.All(geometries, geometry => Assert.InRange(geometry.StartHandle.Bottom, 0, axis.Bounds.Height));
        Capture(host, "native-overlap-timing.png");
        var point = axis.TranslatePoint(geometries[^1].StartHandle.Center, host)!.Value;
        host.MouseDown(point, MouseButton.Left);
        host.MouseMove(point + new Vector(18, 0));
        host.MouseUp(point + new Vector(18, 0), MouseButton.Left);
        Flush(host);
        var changed = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.True(changed.Karaoke[^1].Start > line.Karaoke[^1].Start);
        Assert.Equal(line.Karaoke[^1].End, changed.Karaoke[^1].End);
        Assert.Same(line.Karaoke[0], changed.Karaoke[0]);
        Assert.Same(line.Karaoke[1], changed.Karaoke[1]);
        Assert.Equal(line.Karaoke.Select(clip => clip.Id), axis.SelectedClipIds);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task LinkedDurationFieldMovesTextFollowingGroupsWhileEndInputRemainsIndependent()
    {
        await using var context = new MainWindowTestContext();
        var original = PrepareMultiple(context);
        var line = original.Subtitles[0];
        var host = await Open(context);
        UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(0, 1);
        Flush(host);
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        var popup = Assert.IsType<DraftPopup>(FlyoutBase.GetAttachedFlyout(axis));
        ClickAxis(host, axis, line.Karaoke[0].Id, RawInputModifiers.None);
        Assert.True(popup.IsOpen);
        var content = Assert.IsAssignableFrom<Control>(popup.Child);
        var linked = UiTestActions.Find<CheckBox>(content, "LinkedKaraokeDurationToggle");
        ClickControl(linked);
        Assert.True(context.Session.Details.LinkedDurationEnabled);
        Assert.Equal(Localization.Get("Workbench.LinkedKaraokeDuration"), linked.Content);
        var duration = UiTestActions.Find<NumericDraftInput>(content, "KaraokeDurationInput");
        Assert.True(duration.FocusInput());
        duration.RawText = "2";
        duration.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Flush(host);
        var changed = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.Equal(line.Karaoke[0].Start, changed.Karaoke[0].Start);
        Assert.Equal(line.Karaoke[0].End + new MediaTime(1), changed.Karaoke[0].End);
        Assert.Equal(line.Karaoke[1].Start + new MediaTime(1), changed.Karaoke[1].Start);
        Assert.Equal(line.Karaoke[2].End + new MediaTime(1), changed.Karaoke[2].End);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        popup.ForceClose();
        ClickAxis(host, axis, line.Karaoke[0].Id, RawInputModifiers.None);
        Assert.True(popup.IsOpen);
        var end = UiTestActions.Find<NumericDraftInput>(content, "KaraokeEndInput");
        Assert.True(end.FocusInput());
        end.RawText = "2";
        end.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Flush(host);
        changed = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.Equal(new MediaTime(2), changed.Karaoke[0].End);
        Assert.Same(line.Karaoke[1], changed.Karaoke[1]);
        Assert.Same(line.Karaoke[2], changed.Karaoke[2]);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task UntimedTextHasVisualTimingControlsWithoutAnAssSourcePage()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context);
        var host = await Open(context);
        Assert.True(UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis").IsEffectivelyVisible);
        Assert.True(UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").IsEffectivelyEnabled);
        Assert.DoesNotContain(host.GetVisualDescendants().OfType<Control>(), control => control.Name is "SubtitleCodeInput" or "SubtitleDetailsTabs");
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task ManualCreationTimesOnlyTheSelectedCompleteGraphemeAndHasOneUndo()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context);
        var host = await Open(context);
        UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(1, 3);
        Flush(host);
        UiTestActions.Click(host, "CreateSelectedTimingButton");
        var button = UiTestActions.Find<Button>(host, "CreateSelectedTimingButton");
        var popup = Assert.IsType<DraftPopup>(FlyoutBase.GetAttachedFlyout(button));
        Assert.True(popup.IsOpen);
        var content = Assert.IsAssignableFrom<Control>(popup.Child);
        Capture(Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(content)), "native-manual-timing.png");
        UiTestActions.Find<NumericDraftInput>(content, "CreateTimingStartInput").RawText = "2.25";
        UiTestActions.Find<NumericDraftInput>(content, "CreateTimingEndInput").RawText = "3.75";
        UiTestActions.Find<Button>(content, "ConfirmCreateTimingButton").RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Flush(host);
        Assert.False(popup.IsOpen);
        var clip = Assert.Single(context.Session.Editor.Snapshot.Subtitles[0].Karaoke);
        Assert.Equal(1, clip.Utf16Start);
        Assert.Equal(2, clip.Utf16Length);
        Assert.Equal(new MediaTime(9, 4), clip.Start);
        Assert.Equal(new MediaTime(15, 4), clip.End);
        Assert.Equal(original.Layers, context.Session.Editor.Snapshot.Layers);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task InactiveVisualEditingDoesNotInventTiming()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context);
        var host = await Open(context);
        UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(1, 3);
        UiTestActions.Find<ComboBox>(host, "SubtitleVisualStateInput").SelectedIndex = 1;
        Flush(host);
        var input = UiTestActions.Find<NumericDraftInput>(host, "SelectionStrokeWidthInput");
        Assert.True(input.IsEffectivelyEnabled);
        input.RawText = "5.25";
        input.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Flush(host);
        var line = context.Session.Editor.Snapshot.Subtitles[0];
        Assert.Empty(line.Karaoke);
        Assert.Empty(line.InactiveKaraoke);
        var style = Assert.Single(line.KaraokeStyleSpans);
        Assert.Equal(1, style.Utf16Start);
        Assert.Equal(2, style.Utf16Length);
        Assert.Equal(5.25, style.InactiveStyle!.StrokeWidth);
        Capture(host, "native-inactive-appearance.png");
        Assert.Null(style.ActiveStyle);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
    }

    [AvaloniaFact]
    public async Task SplitButtonExplicitlyDividesTheWholeGroupAndUndoRestoresIt()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context, true);
        var host = await Open(context);
        UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(0, 4);
        Flush(host);
        Assert.Single(context.Session.Editor.Snapshot.Subtitles[0].Karaoke);
        UiTestActions.Click(host, "SplitKaraokeGroupButton");
        Flush(host);
        var clips = context.Session.Editor.Snapshot.Subtitles[0].Karaoke;
        Assert.Equal([1, 2, 1], clips.Select(clip => clip.Utf16Length));
        Capture(host, "native-explicit-split.png");
        Assert.Equal(original.Subtitles[0].Karaoke[0].Start, clips[0].Start);
        Assert.Equal(original.Subtitles[0].Karaoke[0].End, clips[^1].End);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task MovingBetweenTimingInputsKeepsBothEndpointDraftsUntilEnter()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context, true);
        var host = await Open(context);
        UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(0, 4);
        var axis = UiTestActions.Find<KaraokeClipAxis>(host, "KaraokeAxis");
        var popup = Assert.IsType<DraftPopup>(FlyoutBase.GetAttachedFlyout(axis));
        popup.ShowAt(axis);
        var content = Assert.IsAssignableFrom<Control>(popup.Child);
        var start = UiTestActions.Find<NumericDraftInput>(content, "KaraokeStartInput");
        var end = UiTestActions.Find<NumericDraftInput>(content, "KaraokeEndInput");
        Assert.True(start.FocusInput());
        start.RawText = "1.25";
        Assert.True(end.FocusInput());
        end.RawText = "1.5";
        Flush(host);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.Equal("1.25", start.RawText);
        Assert.Equal("1.5", end.RawText);
        end.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Flush(host);
        var clip = Assert.Single(context.Session.Editor.Snapshot.Subtitles[0].Karaoke);
        Assert.Equal(new MediaTime(5, 4), clip.Start);
        Assert.Equal(new MediaTime(3, 2), clip.End);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task InvalidManualTimingStaysOpenAndCancelLeavesNoUndo()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context);
        var host = await Open(context);
        UiTestActions.Find<RichSubtitleEditor>(host, "RichSubtitleInput").SetSelection(1, 3);
        UiTestActions.Click(host, "CreateSelectedTimingButton");
        var popup = Assert.IsType<DraftPopup>(FlyoutBase.GetAttachedFlyout(
            UiTestActions.Find<Button>(host, "CreateSelectedTimingButton")));
        var content = Assert.IsAssignableFrom<Control>(popup.Child);
        UiTestActions.Find<NumericDraftInput>(content, "CreateTimingStartInput").RawText = "3";
        UiTestActions.Find<NumericDraftInput>(content, "CreateTimingEndInput").RawText = "2";
        UiTestActions.Find<Button>(content, "ConfirmCreateTimingButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Flush(host);
        Assert.True(popup.IsOpen);
        Assert.NotNull(context.Session.Details.Error);
        Assert.Same(original, context.Session.Editor.Snapshot);
        UiTestActions.Find<Button>(content, "CancelCreateTimingButton")
            .RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Flush(host);
        Assert.False(popup.IsOpen);
        Assert.Null(context.Session.Details.Error);
        Assert.Same(original, context.Session.Editor.Snapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    private static ProjectDocument Prepare(MainWindowTestContext context, bool grouped = false)
    {
        var line = new SubtitleLine
        {
            Text = "a😀b", End = new(4), Style = new() { FontSize = 32 },
            Karaoke = grouped ? [new(0, 4, new(1, 3), new(10, 3), SceneColor.White)] : []
        };
        var document = new ProjectDocument
        {
            Subtitles = [line],
            Layers = [new() { Id = line.Id, Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End }]
        };
        context.Session.Editor.Reset(document);
        context.Session.SelectCue(line.Id);
        return document;
    }

    private static ProjectDocument PrepareMultiple(MainWindowTestContext context, bool overlapping = false)
    {
        var document = Prepare(context);
        var line = document.Subtitles[0] with
        {
            Karaoke = overlapping
                ? [new(0, 1, new(1, 2), new(5, 2), SceneColor.White),
                    new(1, 2, new(1), new(3), SceneColor.White),
                    new(3, 1, new(3, 2), new(7, 2), SceneColor.White)]
                : [new(0, 1, new(1, 3), new(4, 3), SceneColor.White),
                    new(1, 2, new(4, 3), new(7, 3), SceneColor.White),
                    new(3, 1, new(7, 3), new(10, 3), SceneColor.White)]
        };
        document = document with { Subtitles = [line] };
        context.Session.Editor.Reset(document);
        context.Session.SelectCue(line.Id);
        return document;
    }

    private static void ClickAxis(Window host, KaraokeClipAxis axis, Guid id, RawInputModifiers modifiers)
    {
        axis.BringIntoView();
        Flush(host);
        var point = axis.TranslatePoint(axis.GeometryFor(id).Body.Center, host)!.Value;
        host.MouseDown(point, MouseButton.Left, modifiers);
        host.MouseUp(point, MouseButton.Left, modifiers);
        Flush(host);
    }

    private static void ClickControl(Control control)
    {
        var root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(control));
        control.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        root.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        root.UpdateLayout();
        var point = control.TranslatePoint(new(10, control.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point);
        root.MouseDown(point, MouseButton.Left);
        root.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static async Task<Window> Open(MainWindowTestContext context)
    {
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 1050;
        host.Height = 1000;
        Flush(host);
        Capture(host, "native-subtitle-details.png");
        return host;
    }

    private static void Capture(TopLevel root, string fileName)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Assert.True(Path.IsPathFullyQualified(directory));
        Directory.CreateDirectory(directory);
        root.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        using var frame = root.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, fileName), PngBitmapEncoderOptions.Default);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
