using System.Globalization;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class NumericDraftPrecisionUiTests
{
    [AvaloniaTheory]
    [InlineData("1e-100")]
    [InlineData("1e100")]
    public void NonDecimalFiniteDraftDisablesMouseAndKeyboardSpinAndRestoresSpinAfterAnExactValue(string rawText)
    {
        var draft = new NumericValueDraft { PreserveDoublePrecision = true };
        draft.Load(1);
        var input = new NumericDraftInput
        {
            PreserveDoublePrecision = true,
            [!NumericUpDown.ValueProperty] = new Binding(nameof(NumericValueDraft.Value)) { Source = draft, Mode = BindingMode.TwoWay },
            [!NumericDraftInput.RawTextProperty] = new Binding(nameof(NumericValueDraft.RawText)) { Source = draft, Mode = BindingMode.TwoWay }
        };
        var other = new Button { Content = "Other" };
        var window = new Window
        {
            Width = 420,
            Height = 180,
            Content = new StackPanel { Margin = new(12), Spacing = 12, Children = { input, other } }
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            var text = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
            var spinner = Assert.Single(input.GetVisualDescendants().OfType<ButtonSpinner>());
            var increase = Assert.Single(spinner.GetVisualDescendants().OfType<Button>(), button => button.Name == "PART_IncreaseButton");
            var decrease = Assert.Single(spinner.GetVisualDescendants().OfType<Button>(), button => button.Name == "PART_DecreaseButton");
            Assert.True(text.Focus());
            UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
            window.KeyTextInput(rawText);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(rawText, draft.RawText);
            Assert.Equal(rawText, input.RawText);
            Assert.Equal(0, input.Increment);
            Assert.False(increase.IsEffectivelyEnabled);
            Assert.False(decrease.IsEffectivelyEnabled);
            UiTestActions.Press(window, Key.Up);
            UiTestActions.Press(window, Key.Down);
            var point = increase.TranslatePoint(new Point(increase.Bounds.Width / 2, increase.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(other.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(rawText, draft.RawText);
            Assert.Equal(rawText, input.RawText);
            Assert.Equal(rawText, text.Text);
            Assert.Equal(1, input.Value);
            Assert.True(text.Focus());
            UiTestActions.Press(window, Key.A, RawInputModifiers.Control);
            window.KeyTextInput("1");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, input.Increment);
            Assert.True(increase.IsEffectivelyEnabled);
            Assert.True(decrease.IsEffectivelyEnabled);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(2, decimal.Parse(draft.RawText, CultureInfo.CurrentCulture));
            Assert.Equal(draft.RawText, input.RawText);
            Assert.Equal(input.RawText, text.Text);
            Assert.Equal(2, input.Value);
        }
        finally
        {
            window.Close();
        }
    }
}
