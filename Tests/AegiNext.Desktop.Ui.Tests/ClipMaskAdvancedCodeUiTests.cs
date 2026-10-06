using AegiNext.Core.Projects;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ClipMaskAdvancedCodeUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AdvancedCodeKeyboardEditCommitsMaskAndItsAnimationAtomicallyAndOneUndoRestoresEveryPart(bool changeText)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        UiTestActions.CreateSubtitle(context, text: "Original text");
        var session = context.Session;
        var layerId = session.SelectedLayer!.Id;
        var mask = new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(500, 400) };
        session.Editor.SetClipMask(layerId, mask);
        session.Editor.SetKeyframe(layerId, AnimationProperty.OPACITY, new(new(1), 0.5));
        var original = session.DocumentSnapshot;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 700;
        var tabs = UiTestActions.Find<TabControl>(host, "SubtitleDetailsTabs");
        tabs.SelectedIndex = 1;
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var input = UiTestActions.Find<TextBox>(host, "SubtitleCodeInput");
        Assert.True(input.Focus());
        UiTestActions.Press(host, Key.A, RawInputModifiers.Control);
        host.KeyTextInput(@"{\clip(30,40,500,400)\t(0,2000,2,\clip(80,100,500,400))}" + (changeText ? "Changed text" : "Original text"));
        Dispatcher.UIThread.RunJobs();
        Assert.Same(original, session.DocumentSnapshot);
        Assert.Null(session.Details.Error);
        UiTestActions.Press(host, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        var changed = session.DocumentSnapshot;
        Assert.Equal(changeText ? "Changed text" : "Original text", changed.Subtitles[0].Text);
        var edited = changed.Layers[0];
        Assert.Equal(new ScenePoint(30, 40), Assert.IsType<RectangleClipMask>(edited.Mask).TopLeft);
        Assert.NotEmpty(edited.Tracks.Where(track => AnimationPropertyMetadata.IsMaskProperty(track.Property)));
        Assert.Same(original.Layers[0].Tracks[0], edited.Tracks.Single(track => track.Property == AnimationProperty.OPACITY));
        Assert.True(session.Editor.Undo());
        Assert.Same(original, session.DocumentSnapshot);
        Assert.Same(mask, session.SelectedLayer!.Mask);
        Assert.Equal(original.Subtitles[0], session.DocumentSnapshot.Subtitles[0]);
        Assert.Equal(original.Layers[0].Tracks, session.SelectedLayer.Tracks);
    }

    [AvaloniaFact]
    public async Task AdvancedCodeContentEditPreservesVectorNodeIdentityAndNativeAnimationWhenMaskTagsAreUnchanged()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        UiTestActions.CreateSubtitle(context, text: "Original text");
        var session = context.Session;
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(200, 20) };
        var third = new MaskNode { Position = new(100, 200) };
        var mask = new VectorClipMask { Contours = [new() { Nodes = [first, second, third] }] };
        session.Editor.SetClipMask(session.SelectedLayer!.Id, mask);
        session.Editor.SetKeyframe(session.SelectedLayer!.Id, new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id), new(new(0), first.Position));
        var original = session.DocumentSnapshot;
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.OPEN_SUBTITLE_DETAILS);
        var host = Assert.Single(context.Window.Layouts.FloatingWindows);
        host.Width = 950;
        host.Height = 700;
        var tabs = UiTestActions.Find<TabControl>(host, "SubtitleDetailsTabs");
        tabs.SelectedIndex = 1;
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var input = UiTestActions.Find<TextBox>(host, "SubtitleCodeInput");
        Assert.Contains("Original text", input.Text, StringComparison.Ordinal);
        var source = input.Text!.Replace("Original text", "Changed text", StringComparison.Ordinal);
        Assert.NotEqual(input.Text, source);
        Assert.True(input.Focus());
        UiTestActions.Press(host, Key.A, RawInputModifiers.Control);
        host.KeyTextInput(source);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(source, input.Text);
        Assert.Null(session.Details.Error);
        Assert.Equal("Changed text", session.PreviewDocument.Subtitles[0].Text);
        Assert.Same(original, session.DocumentSnapshot);
        UiTestActions.Press(host, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("Changed text", session.DocumentSnapshot.Subtitles[0].Text);
        Assert.Same(mask, session.SelectedLayer!.Mask);
        Assert.Same(original.Layers[0].Tracks[0], session.SelectedLayer.Tracks[0]);
        Assert.True(session.Editor.Undo());
        Assert.Same(original, session.DocumentSnapshot);
    }
}
