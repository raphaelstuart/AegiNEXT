using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Media.Playback;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class InvalidAnimationDraftUiTests
{
    [AvaloniaFact]
    public async Task InvalidInputDoesNotRecaptureFocusOrFloodLogAndEscapeRestoresOnlyThatField()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        UiTestActions.SelectAnimationProperty(context.Window, AnimationProperty.OPACITY);
        try
        {
            var input = UiTestActions.Find<NumericDraftInput>(context.Window, "KeyframeValueInput");
            input.BringIntoView();
            context.Window.UpdateLayout();
            var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            Assert.True(text.Focus());
            UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
            context.Window.KeyTextInput("illegal");
            var other = UiTestActions.Find<NumericDraftInput>(context.Window, "RotationInput");
            other.Focus();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("illegal", context.ViewModel.Effects.KeyframeValueText);
            Assert.NotNull(context.ViewModel.Effects.ValidationError);
            Assert.False(text.IsFocused);
            var entries = context.ViewModel.Log.Entries.Count;
            for (var index = 0; index < 10; index++)
            {
                context.ViewModel.Effects.CommitDrafts();
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(entries, context.ViewModel.Log.Entries.Count);
            context.ViewModel.Effects.RotationText = "35";
            Assert.True(text.Focus());
            UiTestActions.Press(context.Window, Key.Escape);
            Assert.NotEqual("illegal", context.ViewModel.Effects.KeyframeValueText);
            Assert.Equal("35", context.ViewModel.Effects.RotationText);
            Assert.True(context.Session.TryCommitDrafts());
            Assert.Equal(35, context.Session.SelectedLayer!.Transform.Rotation);
        }
        finally
        {
            context.ViewModel.Effects.RestoreField("KeyframeValueInput");
            context.ViewModel.Effects.RestoreField("RotationInput");
            context.Session.TryCommitDrafts(false);
        }
    }

    [AvaloniaFact]
    public async Task InvalidDraftAllowsPlaybackAndSeekAndLaterCommitUsesOriginalKeyframeTarget()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        var id = session.SelectedLayer!.Id;
        session.Editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(1), 0.25));
        session.Editor.SetKeyframe(id, AnimationProperty.OPACITY, new(new(3), 0.75));
        Assert.True(session.SelectKeyframe(new(id, AnimationProperty.OPACITY, new(1), new(1))));
        context.ViewModel.Effects.KeyframeValueText = "invalid";
        await session.ExecuteCommandAsync(WorkbenchCommand.PLAY_PAUSE);
        Assert.Equal(VideoPlaybackState.PLAYING, context.Controller.Snapshot.State);
        await session.SeekFromUserAsync(new(3));
        Assert.Equal("invalid", context.ViewModel.Effects.KeyframeValueText);
        Assert.Equal(new MediaTime(1), session.SceneEditing.DraftTarget!.LocalTime);
        context.ViewModel.Effects.KeyframeValueText = "0.4";
        Assert.True(session.TryCommitDrafts());
        var keys = session.SelectedLayer!.Tracks.Single(track => track.Property == AnimationProperty.OPACITY).Keyframes;
        Assert.Equal(0.4, keys[0].Value.Scalar);
        Assert.Equal(0.75, keys[1].Value.Scalar);
    }
}
