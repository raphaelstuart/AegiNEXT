using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Masks;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

/// <summary>验证共享蒙版草稿的实际输入来源和静态枢轴编辑。</summary>
public sealed class SharedMaskDraftOriginUiTests
{
    /// <summary>仅聚焦另一面板不能改变无效输入来源，全局校验仍定位实际输入面板。</summary>
    [AvaloniaTheory]
    [InlineData("effects")]
    [InlineData("masks")]
    public async Task FocusingTheOtherProjectionWithoutTypingKeepsTheActualDraftOriginForGlobalValidation(string origin)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id,
            new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(400, 300) });
        MountBothPanels(context);
        var row = Assert.Single(context.ViewModel.Effects.MaskRows, row => row.Target.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        var effectsRow = FindEffectsRow(context.Window.Panels["effects"], row);
        var masksRow = FindMaskRow(context.Window.Panels["masks"], row);
        var originRow = origin == "effects" ? effectsRow : masksRow;
        var otherRow = origin == "effects" ? masksRow : effectsRow;
        var host = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(originRow));
        var otherHost = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(otherRow));
        var source = session.DocumentSnapshot;
        try
        {
            var text = FocusComponent(host, originRow, false);
            UiTestActions.Press(host, Key.A, RawInputModifiers.Control);
            host.KeyTextInput("unfinished");
            var otherText = FocusComponent(otherHost, otherRow, false);
            Assert.True(otherText.IsFocused);
            Assert.Equal("unfinished", otherText.Text);

            Assert.False(session.TryCommitDrafts());
            Flush(host);
            Flush(otherHost);

            Assert.Equal(origin, context.ViewModel.InvalidPanelId);
            Assert.Equal(row.XFieldKey, context.ViewModel.InvalidFieldKey);
            Assert.True(text.IsFocused);
            Assert.False(otherText.IsFocused);
            Assert.True(DataValidationErrors.GetHasErrors(FindComponent(effectsRow, false)));
            Assert.True(DataValidationErrors.GetHasErrors(FindComponent(masksRow, false)));
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            row.Restore(row.XFieldKey);
            row.Restore(row.YFieldKey);
            session.TryCommitDrafts(false);
        }
    }

    /// <summary>静态枢轴共享草稿与错误，焦点切换保留输入来源，Esc只恢复当前分量。</summary>
    [AvaloniaTheory]
    [InlineData("effects")]
    [InlineData("masks")]
    public async Task StaticPivotSharesDraftsAndErrorsRetainsInputOriginAndEscapeRestoresOnlyTheCurrentField(string origin)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id, new RectangleClipMask
        {
            TopLeft = new(10, 20),
            BottomRight = new(400, 300),
            Transform = new() { Pivot = new(17, 19) }
        });
        MountBothPanels(context);
        var effectsRow = UiTestActions.Find<AnimationPropertyRowControl>(context.Window.Panels["effects"], "EffectMaskPivot");
        var masksRow = Assert.Single(context.Window.Panels["masks"].GetVisualDescendants().OfType<AnimationPropertyRowControl>(),
            control => control.DataContext is MaskVectorField { Target: null });
        var field = Assert.IsType<MaskVectorField>(masksRow.DataContext);
        Assert.Same(context.ViewModel.Effects.MaskPivotX, effectsRow.X);
        Assert.Same(effectsRow.X, masksRow.X);
        Assert.Same(effectsRow.Y, masksRow.Y);
        Assert.Same(effectsRow.X, field.X.Draft);
        Assert.Same(effectsRow.Y, field.Y.Draft);
        Assert.False(effectsRow.ShowAnimationActions);
        Assert.False(masksRow.ShowAnimationActions);
        var originRow = origin == "effects" ? effectsRow : masksRow;
        var otherRow = origin == "effects" ? masksRow : effectsRow;
        var host = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(originRow));
        var otherHost = Assert.IsAssignableFrom<Window>(TopLevel.GetTopLevel(otherRow));
        var source = session.DocumentSnapshot;
        try
        {
            var text = FocusComponent(host, originRow, false);
            originRow.Y!.RawText = "55";
            UiTestActions.Press(host, Key.A, RawInputModifiers.Control);
            host.KeyTextInput("unfinished");
            var otherText = FocusComponent(otherHost, otherRow, false);
            Assert.True(otherText.IsFocused);

            Assert.False(session.TryCommitDrafts());
            Flush(host);
            Flush(otherHost);

            Assert.Equal(origin, context.ViewModel.InvalidPanelId);
            Assert.Equal("MaskPivotX", context.ViewModel.InvalidFieldKey);
            Assert.True(text.IsFocused);
            Assert.True(DataValidationErrors.GetHasErrors(FindComponent(effectsRow, false)));
            Assert.True(DataValidationErrors.GetHasErrors(FindComponent(masksRow, false)));
            Assert.Equal("unfinished", effectsRow.X!.RawText);
            Assert.Equal("unfinished", masksRow.X!.RawText);
            Assert.Same(source, session.DocumentSnapshot);

            UiTestActions.Press(host, Key.Escape);
            Flush(host);
            Flush(otherHost);

            Assert.Equal("17", effectsRow.X.RawText);
            Assert.Equal("17", masksRow.X.RawText);
            Assert.Equal("55", effectsRow.Y!.RawText);
            Assert.Equal("55", masksRow.Y!.RawText);
            Assert.False(DataValidationErrors.GetHasErrors(FindComponent(effectsRow, false)));
            Assert.False(DataValidationErrors.GetHasErrors(FindComponent(masksRow, false)));
            Assert.Same(source, session.DocumentSnapshot);
        }
        finally
        {
            context.ViewModel.Masks.RestoreField("MaskPivotX");
            context.ViewModel.Masks.RestoreField("MaskPivotY");
            session.TryCommitDrafts(false);
        }
    }

    private static void MountBothPanels(MainWindowTestContext context)
    {
        context.ViewModel.Effects.MaskExpanded = true;
        context.Window.Layouts.Activate("masks");
        context.Window.Layouts.Float("masks");
        context.Window.Layouts.Activate("effects");
        Flush(context.Window);
        var masksHost = Assert.Single(context.Window.Layouts.FloatingWindows);
        masksHost.Width = 650;
        masksHost.Height = 1000;
        Flush(masksHost);
    }

    private static AnimationPropertyRowControl FindEffectsRow(Control panel, AnimationPropertyRowViewModel row) =>
        Assert.Single(panel.GetVisualDescendants().OfType<AnimationPropertyRowControl>(), control => ReferenceEquals(control.DataContext, row));

    private static AnimationPropertyRowControl FindMaskRow(Control panel, AnimationPropertyRowViewModel row) =>
        Assert.Single(panel.GetVisualDescendants().OfType<AnimationPropertyRowControl>(), control => control.DataContext is MaskVectorField field && ReferenceEquals(field.Row, row));

    private static NumericDraftInput FindComponent(AnimationPropertyRowControl row, bool y) =>
        Assert.Single(row.GetVisualDescendants().OfType<NumericDraftInput>(), input => input.Name == (y ? row.YInputName : row.XInputName));

    private static TextBox FocusComponent(Window host, AnimationPropertyRowControl row, bool y)
    {
        var input = FindComponent(row, y);
        input.BringIntoView();
        Flush(host);
        var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(text.Focus());
        Flush(host);
        return text;
    }

    private static void Flush(Window host)
    {
        host.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        host.UpdateLayout();
    }
}
