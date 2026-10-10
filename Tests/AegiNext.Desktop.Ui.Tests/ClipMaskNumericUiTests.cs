using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Workspace;
using AegiNext.Desktop.Panels.Masks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ClipMaskNumericUiTests
{
    [AvaloniaFact]
    public async Task MaskNumericInputPreviewsCommitsOnEnterAndEscapeRestoresOnlyItsOwnField()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var session = context.Session;
        session.Editor.SetClipMask(session.SelectedLayer!.Id, new RectangleClipMask { TopLeft = new(10, 20), BottomRight = new(500, 400) });
        var original = session.DocumentSnapshot;
        var section = UiTestActions.Find<StackPanel>(context.Window, "ClipMaskSection");
        section.BringIntoView();
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var panel = UiTestActions.Find<ItemsControl>(context.Window, "MaskGeometryFields");
        Assert.Same(context.ViewModel.Masks, context.Window.Panels["masks"].DataContext);
        Assert.All(panel.GetVisualDescendants().OfType<VectorDraftInput>(), vector => Assert.IsType<MaskVectorField>(vector.DataContext));
        var inputs = panel.GetVisualDescendants().OfType<NumericDraftInput>().ToArray();
        var xInput = Assert.Single(inputs, input => input.Name == "MASK_RECTANGLE_TOP_LEFT.0");
        var yInput = Assert.Single(inputs, input => input.Name == "MASK_RECTANGLE_TOP_LEFT.1");
        xInput.BringIntoView();
        context.Window.UpdateLayout();
        var xText = Assert.Single(xInput.GetVisualDescendants().OfType<TextBox>());
        Assert.True(xText.Focus());
        UiTestActions.Press(context.Window, Key.A, RawInputModifiers.Control);
        context.Window.KeyTextInput("25");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(25, Assert.IsType<RectangleClipMask>(session.PreviewDocument.Layers[0].Mask).TopLeft.X);
        Assert.Same(original, session.DocumentSnapshot);
        yInput.RawText = "invalid";
        Assert.False(session.TryCommitDrafts());
        var yText = Assert.Single(yInput.GetVisualDescendants().OfType<TextBox>());
        yText.Focus();
        UiTestActions.Press(context.Window, Key.Escape);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("25", xInput.RawText);
        Assert.Equal("20", yInput.RawText);
        xText.Focus();
        UiTestActions.Press(context.Window, Key.Enter);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(25, Assert.IsType<RectangleClipMask>(session.DocumentSnapshot.Layers[0].Mask).TopLeft.X);
        session.Editor.Undo();
        Assert.Same(original, session.DocumentSnapshot);
    }
}
