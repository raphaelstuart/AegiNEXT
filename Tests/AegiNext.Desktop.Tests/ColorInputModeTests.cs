using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class ColorInputModeTests
{
    [Fact]
    public void ModeAndLanguageChangesDoNotDirtyCommitOrQuantizeHdr()
    {
        var source = new SceneColor(2.5123456789012345, -0.1, 0.123456789, 0.731234567);
        var draft = new ColorDraft(source);
        var changed = 0;
        var committed = 0;
        draft.Changed += (_, _) => changed++;
        draft.Committed += (_, _) => committed++;
        Assert.Equal(ColorInputMode.HEX, draft.InputMode);
        Assert.True(draft.TryToggleInputMode());
        Assert.Equal(ColorInputMode.RGBA, draft.InputMode);
        Assert.Equal(ColorRgbaCodec.Format(source), draft.InputText);
        draft.RefreshLanguage();
        Assert.True(draft.TryCommit(out var actual));
        Assert.Equal(source, actual);
        Assert.True(draft.TryToggleInputMode());
        Assert.Equal(ColorHexCodec.Format(source, true), draft.InputText);
        Assert.False(draft.IsDirty);
        Assert.Equal(0, changed);
        Assert.Equal(0, committed);
    }

    [Fact]
    public void ValidRgbaSynchronizesHexAndModeSwitchKeepsThePendingCompleteColor()
    {
        var draft = new ColorDraft(SceneColor.White);
        Assert.True(draft.TryToggleInputMode());
        draft.InputText = "32,64,128,128";
        Assert.Equal("#20408080", draft.HexText);
        Assert.True(draft.TryToggleInputMode());
        Assert.Equal("#20408080", draft.InputText);
        Assert.True(draft.IsDirty);
        Assert.True(draft.TryCommit(out var color));
        Assert.True(ColorRgbaCodec.TryParse("32,64,128,128", 1, true, out var expected));
        Assert.Equal(expected, color);
        draft.Restore("ColorInput");
        Assert.True(draft.TryCommit(out color));
        Assert.Equal(SceneColor.White, color);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void InvalidRgbaKeepsModeTextAndTargetUntilRestored()
    {
        var source = new SceneColor(2.5, -0.1, 0.123456789, 0.7);
        var draft = new ColorDraft(source);
        Assert.True(draft.TryToggleInputMode());
        draft.InputText = "300,0,0,";
        Assert.False(draft.TryCommit(out _));
        Assert.False(draft.TryToggleInputMode());
        draft.RefreshLanguage();
        draft.Load(SceneColor.Black, false);
        Assert.Equal("300,0,0,", draft.InputText);
        Assert.Equal(ColorInputMode.RGBA, draft.InputMode);
        Assert.NotNull(draft.Error);
        draft.Restore("ColorInput");
        Assert.True(draft.TryCommit(out var actual));
        Assert.Equal(source, actual);
        Assert.Null(draft.Error);
    }
}
