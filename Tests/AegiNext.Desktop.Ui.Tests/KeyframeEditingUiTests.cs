using AegiNext.Application;
using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class KeyframeEditingUiTests
{
    [AvaloniaFact]
    public async Task AddingAtAnotherPlayheadTimeCreatesAnotherFrameAndUndoRemovesOnlyThatFrame()
    {
        await using var context = new MainWindowTestContext();
        await PrepareSubtitleAsync(context);
        var window = context.Window;
        SetSelectedValue(window, 0.25m);
        UiTestActions.Find<ComboBox>(window, "InterpolationCombo").SelectedIndex = (int)KeyframeInterpolation.EASE_OUT;
        await context.Controller.SeekAsync(new(1, 3));
        UiTestActions.Click(window, "KeyframeButton");
        var first = Assert.Single(OpacityTrack(window.DocumentSnapshot).Keyframes);
        Assert.Equal(new MediaTime(1, 3), first.Time);

        await context.Controller.SeekAsync(new(4, 3));
        UiTestActions.Click(window, "KeyframeButton");
        var frames = OpacityTrack(window.DocumentSnapshot).Keyframes;
        Assert.Equal(2, frames.Length);
        Assert.Equal(first, frames[0]);
        Assert.Equal(new MediaTime(4, 3), frames[1].Time);
        Assert.Equal(0.25, frames[1].Value.Scalar);
        Assert.Equal(KeyframeInterpolation.EASE_OUT, frames[1].Interpolation);

        Execute(window, WorkbenchCommand.UNDO);
        Assert.Equal(first, Assert.Single(OpacityTrack(window.DocumentSnapshot).Keyframes));
        Execute(window, WorkbenchCommand.REDO);
        Assert.Equal(frames.ToArray(), OpacityTrack(window.DocumentSnapshot).Keyframes.ToArray());
    }

    [AvaloniaFact]
    public async Task SelectingTimelineFrameLoadsAndEditsItsValueAndInterpolationWithUndoAndPersistence()
    {
        await using var context = new MainWindowTestContext();
        await PrepareSubtitleAsync(context);
        var window = context.Window;
        var valueInput = UiTestActions.Find<NumericUpDown>(window, "KeyframeValueInput");
        var interpolation = UiTestActions.Find<ComboBox>(window, "InterpolationCombo");
        SetSelectedValue(window, 0.25m);
        interpolation.SelectedIndex = (int)KeyframeInterpolation.EASE_OUT;
        await context.Controller.SeekAsync(new(1));
        UiTestActions.Click(window, "KeyframeButton");
        await context.Controller.SeekAsync(new(2));
        UiTestActions.Click(window, "KeyframeButton");
        SetSelectedValue(window, 0.75m);
        interpolation.SelectedIndex = (int)KeyframeInterpolation.HOLD;
        var second = OpacityTrack(window.DocumentSnapshot).Keyframes[1];
        Assert.Equal(new Keyframe(new(2), 0.75, KeyframeInterpolation.HOLD), second);

        var beforeSelection = window.DocumentSnapshot;
        SelectOpacityFrame(window, new(1), 0.25);
        Assert.Same(beforeSelection, window.DocumentSnapshot);
        Assert.Equal(0.25m, valueInput.Value);
        Assert.Equal((int)KeyframeInterpolation.EASE_OUT, interpolation.SelectedIndex);
        SetSelectedValue(window, 0.5m);
        Assert.Equal(new Keyframe(new(1), 0.5, KeyframeInterpolation.EASE_OUT),
            OpacityTrack(window.DocumentSnapshot).Keyframes[0]);
        interpolation.SelectedIndex = (int)KeyframeInterpolation.LINEAR;
        Assert.Equal(new Keyframe(new(1), 0.5), OpacityTrack(window.DocumentSnapshot).Keyframes[0]);
        Assert.Equal(second, OpacityTrack(window.DocumentSnapshot).Keyframes[1]);

        Execute(window, WorkbenchCommand.UNDO);
        Assert.Equal(new Keyframe(new(1), 0.5, KeyframeInterpolation.EASE_OUT),
            OpacityTrack(window.DocumentSnapshot).Keyframes[0]);
        Assert.Equal((int)KeyframeInterpolation.EASE_OUT, interpolation.SelectedIndex);
        Execute(window, WorkbenchCommand.UNDO);
        Assert.Equal(new Keyframe(new(1), 0.25, KeyframeInterpolation.EASE_OUT),
            OpacityTrack(window.DocumentSnapshot).Keyframes[0]);
        Assert.Equal(0.25m, valueInput.Value);
        Execute(window, WorkbenchCommand.REDO);
        Execute(window, WorkbenchCommand.REDO);
        var restored = ProjectStore.Deserialize(ProjectStore.Serialize(window.DocumentSnapshot));
        Assert.Equal(new Keyframe(new(1), 0.5), OpacityTrack(restored).Keyframes[0]);
        Assert.Equal(second, OpacityTrack(restored).Keyframes[1]);
    }

    [AvaloniaFact]
    public async Task ChangingPropertyKeepsSelectedTimeAndEditsOnlyTheNewProperty()
    {
        await using var context = new MainWindowTestContext();
        await PrepareSubtitleAsync(context);
        var window = context.Window;
        SetSelectedValue(window, 0.25m);
        await context.Controller.SeekAsync(new(1));
        UiTestActions.Click(window, "KeyframeButton");
        var before = window.DocumentSnapshot;
        UiTestActions.SelectAnimationProperty(window, AnimationProperty.POSITION);
        SetSelectedValue(window, 120, "KeyframeValueXInput");
        UiTestActions.Find<ComboBox>(window, "InterpolationCombo").SelectedIndex = (int)KeyframeInterpolation.HOLD;
        Assert.NotSame(before, window.DocumentSnapshot);

        UiTestActions.Click(window, "KeyframeButton");
        var tracks = Assert.Single(window.DocumentSnapshot.Layers).Tracks;
        Assert.Equal(2, tracks.Length);
        Assert.Equal(0.25, Assert.Single(OpacityTrack(window.DocumentSnapshot).Keyframes).Value.Scalar);
        Assert.Equal(new Keyframe(new(1), new ScenePoint(120, 0), KeyframeInterpolation.HOLD),
            Assert.Single(tracks.Single(track => track.Property == AnimationProperty.POSITION).Keyframes));
    }

    private static async Task PrepareSubtitleAsync(MainWindowTestContext context)
    {
        await context.OpenMediaAsync();
        context.Window.GetCommand(AegiNext.Desktop.Shortcuts.WorkbenchCommand.VIEW_EFFECTS).Execute(null);
        await UiTestActions.CreateSubtitleAsync(context);
        UiTestActions.SelectAnimationProperty(context.Window, AnimationProperty.OPACITY);
        context.Window.UpdateLayout();
    }

    private static AnimationTrack OpacityTrack(ProjectDocument document)
    {
        return Assert.Single(document.Layers).Tracks.Single(track => track.Property == AnimationProperty.OPACITY);
    }

    private static void SetSelectedValue(MainWindow window, decimal value, string fieldName = "KeyframeValueInput")
    {
        var input = UiTestActions.Find<NumericUpDown>(window, fieldName);
        input.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var textBox = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(textBox.Focus());
        textBox.Text = value.ToString(System.Globalization.CultureInfo.CurrentCulture);
        Dispatcher.UIThread.RunJobs();
        Assert.True(UiTestActions.Find<SubtitleTimelineControl>(window, "Timeline").Focus());
        Dispatcher.UIThread.RunJobs();
    }

    private static void SelectOpacityFrame(MainWindow window, MediaTime time, double value)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(window, "Timeline");
        var layer = Assert.Single(window.DocumentSnapshot.Layers);
        var keyPoint = timeline.GetKeyframePoint(layer.Id, time, value)
            ?? throw new InvalidOperationException("The selected clip's animation is not expanded.");
        var point = timeline.TranslatePoint(keyPoint, window)
            ?? throw new InvalidOperationException("Timeline is not attached to the test window.");
        Assert.Same(timeline, window.InputHitTest(point));
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Execute(MainWindow window, WorkbenchCommand command)
    {
        var action = window.GetCommand(command);
        Assert.True(action.CanExecute(null));
        action.Execute(null);
        Dispatcher.UIThread.RunJobs();
    }
}
