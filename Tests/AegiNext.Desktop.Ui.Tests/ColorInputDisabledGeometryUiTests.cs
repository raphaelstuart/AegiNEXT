using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ColorInputDisabledGeometryUiTests
{
    [AvaloniaTheory]
    [InlineData(false, 150)]
    [InlineData(true, 150)]
    [InlineData(false, 220)]
    [InlineData(true, 220)]
    public void DisabledNarrowColorBarHasNoOverlappingCellsOrSeparateDisabledBackgrounds(bool dark, double width)
    {
        using var environment = new UiTestEnvironment();
        var input = new ColorDraftInput { Draft = new ColorDraft(SceneColor.White), Width = width, IsEnabled = false };
        var window = new Window
        {
            Width = 360, Height = 150, Content = input,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Classes = { "business-surface" }
        };
        try
        {
            window.Show();
            window.UpdateLayout();
            var text = input.FindControl<TextBox>("ColorInput")!;
            var mode = input.FindControl<Button>("ModeButton")!;
            var picker = input.FindControl<Button>("PickerButton")!;
            var cells = new Control[] { input.FindControl<ColorPreviewer>("ColorPreview")!, text, mode, picker };
            for (var index = 1; index < cells.Length; index++)
            {
                var left = cells[index - 1].TranslatePoint(default, input)!.Value.X;
                var right = cells[index].TranslatePoint(default, input)!.Value.X;
                Assert.Equal(left + cells[index - 1].Bounds.Width, right, 5);
            }
            var border = text.GetVisualDescendants().OfType<Border>().Single(value => value.Name == "PART_BorderElement");
            Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color);
            foreach (var button in new[] { mode, picker })
            {
                var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
                    .Single(value => value.Name == "PART_ContentPresenter");
                Assert.Equal(Colors.Transparent, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
            }
            Assert.True(text.ClipToBounds);
            Assert.False(input.Draft!.IsDirty);
        }
        finally
        {
            window.Close();
        }
    }
}
