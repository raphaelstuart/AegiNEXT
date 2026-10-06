using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ColorInputBarUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void BarIsContiguousWithSquareControlsAndNativePickerWorksInBothThemes(bool dark)
    {
        using var environment = new UiTestEnvironment();
        var draft = new ColorDraft(new(0.25, 0.5, 0.75, 0.5));
        var input = new ColorDraftInput { Draft = draft };
        var window = new Window
        {
            Width = 360, Height = 170, Content = input, Padding = new(16),
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Classes = { "business-surface" }
        };
        var commits = new List<SceneColor>();
        draft.Committed += (_, args) =>
        {
            commits.Add(args.Value);
            draft.Load(args.Value);
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            var preview = input.FindControl<ColorPreviewer>("ColorPreview")!;
            var text = input.FindControl<TextBox>("ColorInput")!;
            var mode = input.FindControl<Button>("ModeButton")!;
            var pickerButton = input.FindControl<Button>("PickerButton")!;
            var picker = input.FindControl<ColorView>("Picker")!;
            var controls = new Control[] { preview, text, mode, pickerButton };
            foreach (var square in new Control[] { preview, mode, pickerButton })
            {
                Assert.Equal(32, square.Bounds.Width);
                Assert.Equal(32, square.Bounds.Height);
            }
            for (var index = 1; index < controls.Length; index++)
            {
                var previous = controls[index - 1].TranslatePoint(default, window)!.Value;
                var current = controls[index].TranslatePoint(default, window)!.Value;
                Assert.Equal(previous.X + controls[index - 1].Bounds.Width, current.X, 5);
                Assert.Equal(previous.Y, current.Y, 5);
            }
            Assert.Empty(commits);
            Click(window, mode);
            Assert.Equal(ColorInputMode.RGBA, draft.InputMode);
            Assert.Equal(ColorRgbaCodec.Format(draft.Value), text.Text);
            Capture(window, dark ? "color-input-rgba-dark.png" : "color-input-rgba-light.png");
            text.Focus();
            text.SelectAll();
            window.KeyTextInput("32,64,128,128");
            UiTestActions.Press(window, Key.Enter);
            Dispatcher.UIThread.RunJobs();
            Assert.True(ColorRgbaCodec.TryParse("32,64,128,128", 1, true, out var expected));
            Assert.Equal(expected, Assert.Single(commits));
            Assert.Equal(picker.Color.ToHsv(), preview.HsvColor);
            Click(window, mode);
            Assert.Equal("#20408080", text.Text);
            Assert.Single(commits);
            Click(window, pickerButton);
            Assert.True(pickerButton.Flyout!.IsOpen);
            Assert.True(picker.IsAttachedToVisualTree());
            Assert.Equal(Color.Parse("#80204080"), picker.Color);
            Assert.Single(commits);
            picker.Color = Color.Parse("#C54885");
            Assert.Equal("#C54885FF", text.Text);
            Assert.Equal(2, commits.Count);
            pickerButton.Flyout.Hide();
            Assert.False(pickerButton.Flyout.IsOpen);
            Capture(window, dark ? "color-input-hex-dark.png" : "color-input-hex-light.png");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void InvalidRgbaCannotBeHiddenByModeSwitchAndEscapeRestoresWithoutCommitting()
    {
        using var environment = new UiTestEnvironment();
        var source = new SceneColor(2.5123456789012345, -0.1, 0.3456789012345678, 0.7312345678901234);
        var draft = new ColorDraft(source);
        var input = new ColorDraftInput { Draft = draft };
        var window = new Window { Width = 360, Height = 180, Content = input, Padding = new(16) };
        var commits = 0;
        draft.Committed += (_, _) => commits++;
        try
        {
            window.Show();
            var mode = input.FindControl<Button>("ModeButton")!;
            var text = input.FindControl<TextBox>("ColorInput")!;
            var pickerButton = input.FindControl<Button>("PickerButton")!;
            Click(window, pickerButton);
            Assert.True(pickerButton.Flyout!.IsOpen);
            Assert.Equal(source, draft.Value);
            Assert.False(draft.IsDirty);
            Assert.Equal(0, commits);
            pickerButton.Flyout.Hide();
            Click(window, mode);
            text.Focus();
            text.SelectAll();
            window.KeyTextInput("256,0,0,");
            UiTestActions.Press(window, Key.Enter);
            Click(window, mode);
            Assert.Equal(ColorInputMode.RGBA, draft.InputMode);
            Assert.Equal("256,0,0,", text.Text);
            Assert.True(draft.HasError);
            Dispatcher.UIThread.RunJobs();
            Assert.False(text.IsFocused);
            text.Focus();
            UiTestActions.Press(window, Key.Escape);
            Assert.Equal(source, draft.Value);
            Assert.Equal(ColorRgbaCodec.Format(source), text.Text);
            Assert.False(draft.HasError);
            Assert.False(draft.IsDirty);
            Assert.Equal(0, commits);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Click(Window window, Button button)
    {
        window.UpdateLayout();
        var point = button.TranslatePoint(new Point(16, 16), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    private static void Capture(Window window, string name)
    {
        var directory = Environment.GetEnvironmentVariable("AEGINEXT_UI_CAPTURE_DIRECTORY");
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        frame.Save(Path.Combine(directory, name), PngBitmapEncoderOptions.Default);
    }
}
