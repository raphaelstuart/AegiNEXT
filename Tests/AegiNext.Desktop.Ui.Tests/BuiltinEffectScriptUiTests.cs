using AegiNext.Core.Editing;
using AegiNext.Core.Effects;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Headless.XUnit;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class BuiltinEffectScriptUiTests
{
    [AvaloniaTheory]
    [InlineData("fade-in-out")]
    [InlineData("pop-in")]
    [InlineData("slide-in")]
    [InlineData("fade-in")]
    [InlineData("fade-out")]
    [InlineData("pop-out")]
    [InlineData("slide-out")]
    public async Task PresetSelectorAppliesTheEmbeddedScriptWithOneUndo(string id)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var cueId = context.Session.Editor.AddSubtitle(new(1), new(7, 5), "脚本字幕");
        context.Session.SelectCue(cueId);
        Assert.Equal(cueId, context.Session.SelectedCueId);
        context.Window.GetCommand(WorkbenchCommand.VIEW_EFFECTS).Execute(null);
        var before = context.Session.DocumentSnapshot;
        var expected = EffectScriptCompiler.Compile(BuiltinEffectScripts.Get(id).Script, before.Layers[0]);

        UiTestActions.SelectBuiltinPreset(context.Window, id);
        UiTestActions.Click(context.Window, "ApplyPresetButton");

        var actual = Assert.Single(context.Session.DocumentSnapshot.Layers);
        Assert.Equal(cueId, actual.Id);
        Assert.Equal(expected.SelectMany(track => track.Keyframes), actual.Tracks.SelectMany(track => track.Keyframes));
        Assert.Equal(expected.Select(track => track.Property), actual.Tracks.Select(track => track.Property));
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(before, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task ApplyingFadeInThenFadeOutThroughThePresetSelectorKeepsBothEdgesAndOneUndoPerApplication()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var cueId = context.Session.Editor.AddSubtitle(new(1), new(5), "组合淡入淡出");
        context.Session.SelectCue(cueId);
        context.Window.GetCommand(WorkbenchCommand.VIEW_EFFECTS).Execute(null);

        UiTestActions.SelectBuiltinPreset(context.Window, "fade-in");
        UiTestActions.Click(context.Window, "ApplyPresetButton");
        var entrance = context.Session.DocumentSnapshot;
        Assert.Equal(0.75, Assert.Single(SceneEvaluator.Evaluate(entrance, new(23, 20))).Opacity, 12);
        Assert.Equal(1, Assert.Single(SceneEvaluator.Evaluate(entrance, new(97, 20))).Opacity, 12);

        UiTestActions.SelectBuiltinPreset(context.Window, "fade-out");
        UiTestActions.Click(context.Window, "ApplyPresetButton");

        var composed = context.Session.DocumentSnapshot;
        Assert.Equal(0.75, Assert.Single(SceneEvaluator.Evaluate(composed, new(23, 20))).Opacity, 12);
        Assert.Equal(1, Assert.Single(SceneEvaluator.Evaluate(composed, new(3))).Opacity, 12);
        Assert.Equal(0.75, Assert.Single(SceneEvaluator.Evaluate(composed, new(97, 20))).Opacity, 12);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(entrance, context.Session.DocumentSnapshot);
        Assert.Equal(0.75, Assert.Single(SceneEvaluator.Evaluate(context.Session.DocumentSnapshot, new(23, 20))).Opacity, 12);
        Assert.Equal(1, Assert.Single(SceneEvaluator.Evaluate(context.Session.DocumentSnapshot, new(97, 20))).Opacity, 12);
        Assert.True(context.Session.Editor.Redo());
        Assert.Same(composed, context.Session.DocumentSnapshot);
    }
}
