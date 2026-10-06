using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Styling;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;
using Material.Icons.Avalonia;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class ToolbarToggleButtonUiTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void StandaloneToggleSupportsContentAndStateWithoutPanelClasses(bool dark, bool icon)
    {
        using var environment = new UiTestEnvironment();
        var commands = 0;
        var clicks = 0;
        var button = new ToolbarToggleButton
        {
            Content = icon ? WorkbenchIcon.Create("Loop") : "B",
            IsChecked = false,
            Command = new RelayCommand(() => commands++)
        };
        button.Click += (_, _) => clicks++;
        var window = new Window
        {
            Width = 160,
            Height = 100,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light,
            Content = button
        };
        try
        {
            window.Show();
            Flush(window);
            Assert.NotNull(button.Template);
            Assert.Equal(new Size(32, 32), button.Bounds.Size);
            Assert.Equal(HorizontalAlignment.Center, button.HorizontalContentAlignment);
            Assert.Equal(VerticalAlignment.Center, button.VerticalContentAlignment);
            var uncheckedBorder = Assert.IsAssignableFrom<ISolidColorBrush>(button.BorderBrush).Color;
            button.IsChecked = true;
            Flush(window);
            Assert.Equal(0, commands);
            Assert.Equal(0, clicks);
            Assert.NotEqual(uncheckedBorder, Assert.IsAssignableFrom<ISolidColorBrush>(button.BorderBrush).Color);
            var checkedBackground = button.Background;
            button.IsChecked = false;
            Assert.True(button.Focus());
            var point = button.TranslatePoint(new Point(16, 16), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            window.MouseMove(new(window.ClientSize.Width - 1, window.ClientSize.Height - 1));
            Flush(window);
            Assert.True(button.IsChecked);
            Assert.Equal(1, commands);
            Assert.Equal(1, clicks);
            Assert.Equal(checkedBackground, button.Background);
            var presenter = Assert.Single(button.GetVisualDescendants().OfType<ContentPresenter>(), value => value.Name == "PART_ContentPresenter");
            Assert.Equal(button.Background, presenter.Background);
            Assert.Equal(button.BorderBrush, presenter.BorderBrush);
            if (icon)
            {
                var material = Assert.IsType<MaterialIcon>(button.Content);
                Assert.Equal(button.Foreground, material.Foreground);
                Assert.Equal(button.Foreground, material.Drawing.Brush);
            }
            window.MouseMove(point);
            Flush(window);
            Assert.Equal(button.Background, presenter.Background);
            Assert.Equal(button.Foreground, presenter.Foreground);
            window.MouseMove(new(window.ClientSize.Width - 1, window.ClientSize.Height - 1));
            window.RequestedThemeVariant = dark ? ThemeVariant.Light : ThemeVariant.Dark;
            Flush(window);
            Assert.True(button.IsChecked);
            Assert.Equal(new Size(32, 32), button.Bounds.Size);
            Assert.Equal(1, commands);
            if (icon)
            {
                var material = Assert.IsType<MaterialIcon>(button.Content);
                Assert.Equal(button.Foreground, material.Foreground);
                Assert.Equal(button.Foreground, material.Drawing.Brush);
            }
            UiTestActions.Press(window, Key.Space);
            Assert.False(button.IsChecked);
            Assert.Equal(2, commands);
            Assert.Equal(2, clicks);
            button.IsEnabled = false;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            UiTestActions.Press(window, Key.Space);
            Assert.False(button.IsChecked);
            Assert.Equal(2, commands);
            Assert.Equal(2, clicks);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Flush(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
