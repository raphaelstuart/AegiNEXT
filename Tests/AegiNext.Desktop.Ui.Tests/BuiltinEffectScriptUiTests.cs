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
}
