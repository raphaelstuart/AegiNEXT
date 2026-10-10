using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class NativeKaraokeDetailsUiTests
{
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

    private static async Task<Window> Open(MainWindowTestContext context)
    {
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 1050;
        host.Height = 1000;
        Flush(host);
        return host;
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
