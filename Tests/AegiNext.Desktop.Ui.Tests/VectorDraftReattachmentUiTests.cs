using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Panels.Masks;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class VectorDraftReattachmentUiTests
{
    [AvaloniaFact]
    public void DataTemplateReattachmentPreservesAuthoritativeRawAndSpinnerChangesOnlyItsComponent()
    {
        var target = new AnimationTrackTarget(AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        var x = new MaskNumericField("X", "Workbench.MASK_RECTANGLE_TOP_LEFT", target, 0, -1000, 1000);
        var y = new MaskNumericField("Y", "Workbench.MASK_RECTANGLE_TOP_LEFT", target, 1, -1000, 1000);
        x.Draft.Load(10);
        y.Draft.Load(20);
        var model = new MaskVectorField(x, y);
        var content = new ContentControl
        {
            Content = model,
            ContentTemplate = new FuncDataTemplate<MaskVectorField>((field, _) => new VectorDraftInput
            {
                XFieldKey = "X",
                YFieldKey = "Y",
                [!VectorDraftInput.XProperty] = new Binding("X.Draft.Value") { Source = field, Mode = BindingMode.TwoWay },
                [!VectorDraftInput.YProperty] = new Binding("Y.Draft.Value") { Source = field, Mode = BindingMode.TwoWay },
                [!VectorDraftInput.XTextProperty] = new Binding("X.Draft.RawText") { Source = field, Mode = BindingMode.TwoWay },
                [!VectorDraftInput.YTextProperty] = new Binding("Y.Draft.RawText") { Source = field, Mode = BindingMode.TwoWay }
            })
        };
        var first = new Window { Width = 500, Height = 180, Content = content };
        var second = new Window { Width = 360, Height = 180 };
        first.Show();
        try
        {
            first.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var vector = Assert.Single(content.GetVisualDescendants().OfType<VectorDraftInput>());
            var xInput = Assert.Single(vector.GetVisualDescendants().OfType<NumericDraftInput>(), input => input.Name == "X");
            var yInput = Assert.Single(vector.GetVisualDescendants().OfType<NumericDraftInput>(), input => input.Name == "Y");
            Assert.Equal("10", xInput.RawText);
            Assert.Equal("20", yInput.RawText);
            var text = Assert.Single(xInput.GetVisualDescendants().OfType<TextBox>());
            Assert.True(text.Focus());
            UiTestActions.Press(first, Key.A, RawInputModifiers.Control);
            first.KeyTextInput("invalid X");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("invalid X", x.Draft.RawText);
            first.Content = null;
            second.Content = content;
            second.Show();
            second.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("invalid X", x.Draft.RawText);
            Assert.Equal("20", y.Draft.RawText);
            vector = Assert.Single(content.GetVisualDescendants().OfType<VectorDraftInput>());
            xInput = Assert.Single(vector.GetVisualDescendants().OfType<NumericDraftInput>(), input => input.Name == "X");
            yInput = Assert.Single(vector.GetVisualDescendants().OfType<NumericDraftInput>(), input => input.Name == "Y");
            Assert.Equal("invalid X", vector.XText);
            Assert.Equal("20", vector.YText);
            Assert.Equal("invalid X", xInput.RawText);
            Assert.Equal("20", yInput.RawText);
            Assert.Equal("invalid X", Assert.Single(xInput.GetVisualDescendants().OfType<TextBox>()).Text);
            var spinner = Assert.Single(yInput.GetVisualDescendants().OfType<ButtonSpinner>());
            var increase = Assert.Single(spinner.GetVisualDescendants().OfType<Button>(), button => button.Name == "PART_IncreaseButton");
            Assert.True(increase.IsEffectivelyEnabled);
            var point = increase.TranslatePoint(new Point(increase.Bounds.Width / 2, increase.Bounds.Height / 2), second)!.Value;
            second.MouseDown(point, MouseButton.Left);
            second.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("21", y.Draft.RawText);
            Assert.Equal("invalid X", x.Draft.RawText);
        }
        finally
        {
            first.Close();
            second.Close();
        }
    }
}
