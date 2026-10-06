using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;

namespace AegiNext.Desktop.Tests;

public sealed class ColorDraftTests
{
    [Fact]
    public void ExplicitSixDigitHexMakesTheColorOpaqueWhileDisplayLoadPreservesAlpha()
    {
        var source = new SceneColor(0.3, 0.4, 0.5, 0.7312345678901234);
        var draft = new ColorDraft(source);
        Assert.True(draft.TryCommit(out var unedited));
        Assert.Equal(source, unedited);
        draft.HexText = "#FF0000";
        Assert.True(draft.TryCommit(out var edited));
        Assert.Equal(new SceneColor(1, 0, 0, 1), edited);
    }

    [Fact]
    public void LoadingAndValidationPreserveExactHdrValuesWithoutUserEvents()
    {
        var source = new SceneColor(2.5123456789012345, -0.13123456789012345, 0.3456789012345678, 0.7312345678901234);
        var draft = new ColorDraft();
        var changed = 0;
        var committed = 0;
        draft.Changed += (_, _) => changed++;
        draft.Committed += (_, _) => committed++;
        draft.Load(source);
        draft.RefreshLanguage();
        Assert.True(draft.TryCommit(out var actual));
        Assert.Equal(source, actual);
        Assert.Equal(source, draft.Value);
        Assert.False(draft.IsDirty);
        Assert.Equal(0, changed);
        Assert.Equal(0, committed);
    }

    [Fact]
    public void ExplicitUserValueIsPreparedExactlyAndOnlyCommitsWhenRequested()
    {
        var draft = new ColorDraft(SceneColor.Black);
        var next = new SceneColor(0.12345678901234566, 0.2, 2.4123456789012345, 0.7312345678901234);
        var changes = 0;
        var values = new List<SceneColor>();
        draft.Changed += (_, _) => changes++;
        draft.Committed += (_, args) => values.Add(args.Value);
        draft.SetValue(next);
        Assert.Equal(1, changes);
        Assert.Empty(values);
        Assert.True(draft.TryCommit(out var prepared));
        Assert.Equal(next, prepared);
        Assert.Empty(values);
        Assert.True(draft.TryCommit());
        Assert.Equal(next, Assert.Single(values));
        draft.Load(next);
        Assert.False(draft.IsDirty);
        Assert.Equal(1, changes);
    }

    [Fact]
    public void InvalidHexIsPreservedAndEscapeRestoresExactOriginal()
    {
        var source = new SceneColor(2.5, -0.1, 0.12345678901234566, 0.7);
        var draft = new ColorDraft(source) { HexText = "#broken" };
        Assert.False(draft.TryCommit(out _));
        Assert.Equal("#broken", draft.HexText);
        Assert.Equal(source, draft.Value);
        Assert.NotNull(draft.Error);
        draft.Load(SceneColor.Black, false);
        Assert.Equal("#broken", draft.HexText);
        draft.Restore("HexText");
        Assert.True(draft.TryCommit(out var actual));
        Assert.Equal(source, actual);
        Assert.Null(draft.Error);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void SingleFieldRestoreKeepsOtherUnfinishedAndValidLinearFields()
    {
        var source = new SceneColor(2.5, -0.13123456789012345, 0.3456789012345678, 0.7312345678901234);
        var draft = new ColorDraft(source);
        draft.Red.RawText = "7e-";
        draft.Green.RawText = "1.2";
        Assert.False(draft.TryCommit(out _));
        Assert.Equal("Red", draft.InvalidFieldKey);
        draft.Restore("Red");
        Assert.True(draft.TryCommit(out var actual));
        Assert.Equal(source with { Green = 1.2 }, actual);
        Assert.True(draft.IsDirty);
        Assert.Null(draft.Error);
    }

    [Fact]
    public void HexThenLinearEditRetainsOtherNewComponentsAndAlpha()
    {
        var draft = new ColorDraft(new SceneColor(2.5, -0.1, 0.1, 0.7)) { HexText = "#20408080" };
        Assert.True(ColorHexCodec.TryParse(draft.HexText, 0.7, true, out var decoded));
        draft.Red.RawText = "2.5";
        Assert.True(draft.TryCommit(out var actual));
        Assert.Equal(decoded with { Red = 2.5 }, actual);
    }

    [Fact]
    public void CancellingInvalidHexPreservesASeparateInvalidNumericDraft()
    {
        var draft = new ColorDraft();
        draft.Green.RawText = "7e-";
        draft.HexText = "#bad";
        draft.Restore("HexText");
        Assert.Equal("7e-", draft.Green.RawText);
        Assert.False(draft.TryCommit(out _));
        Assert.Equal("Green", draft.InvalidFieldKey);
        draft.Restore("GreenInput");
        Assert.True(draft.TryCommit(out var actual));
        Assert.Equal(SceneColor.White, actual);
        Assert.False(draft.IsDirty);
    }

    [Fact]
    public void DisabledAlphaIgnoresUserAlphaWithoutChangingTheLoadedAlpha()
    {
        var source = new SceneColor(1, 0, 0, 0.7312345678901234);
        var draft = new ColorDraft(source) { IsAlphaEnabled = false, HexText = "#0000FF00" };
        Assert.True(draft.TryCommit(out var actual));
        Assert.Equal(new SceneColor(0, 0, 1, source.Alpha), actual);
        draft.SetValue(new(1, 0.25, 0.5, 0));
        Assert.True(draft.TryCommit(out actual));
        Assert.Equal(source.Alpha, actual.Alpha);
    }
}
