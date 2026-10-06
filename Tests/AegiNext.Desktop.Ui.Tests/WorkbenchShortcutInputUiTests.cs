using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Controls.Common;
using AegiNext.Desktop.Menus;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Windowing;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class WorkbenchShortcutInputUiTests
{
    [AvaloniaFact]
    public void FocusedClosedMenuRetainsGlobalShortcutsWhileExpandedMenuUsesLocalInput()
    {
        using var environment = new UiTestEnvironment();
        var enterCount = 0;
        var playCount = 0;
        var commands = CreateCommands();
        commands[WorkbenchCommand.TIMING_ENTER] = new(() => enterCount++, () => true);
        commands[WorkbenchCommand.PLAY_PAUSE] = new(() => playCount++, () => true);
        using var registry = new WorkbenchWindowRegistry(new(id => commands[id]), () => { });
        var item = new MenuItem { Header = "File", Items = { new MenuItem { Header = "Open" } } };
        var menu = new Menu { Items = { item } };
        var window = new Window { Width = 600, Height = 360, Content = menu };
        try
        {
            registry.Register(window, () => "AegiNext");
            window.Show();
            window.UpdateLayout();
            Assert.True(item.Focus());
            Assert.False(menu.IsOpen);
            Assert.False(item.IsSubMenuOpen);

            UiTestActions.Press(window, Key.F8);
            UiTestActions.Press(window, Key.Space);

            Assert.Equal(1, enterCount);
            Assert.Equal(1, playCount);
            Assert.False(item.IsSubMenuOpen);
            item.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            UiTestActions.Press(window, Key.F8);
            Assert.Equal(1, enterCount);
        }
        finally
        {
            item.IsSubMenuOpen = false;
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Button")]
    [InlineData("CheckBox")]
    [InlineData("Anchor")]
    [InlineData("ToolbarToggle")]
    public void FocusedControlDoesNotClickWhenSpaceIsConsumedForPlayback(string controlKind)
    {
        using var environment = new UiTestEnvironment();
        var playCount = 0;
        var clickCount = 0;
        var commands = CreateCommands();
        commands[WorkbenchCommand.PLAY_PAUSE] = new(() => playCount++, () => true);
        using var registry = new WorkbenchWindowRegistry(new(id => commands[id]), () => { });
        Button button = controlKind switch
        {
            "CheckBox" => new CheckBox { Content = "Toggle" },
            "Anchor" => new AnchorPresetButton { Content = "Anchor" },
            "ToolbarToggle" => new ToolbarToggleButton { Content = "Toggle" },
            _ => new Button { Content = "Action" }
        };
        button.Click += (_, _) => clickCount++;
        var window = new Window { Width = 600, Height = 360, Content = button };
        try
        {
            registry.Register(window, () => "AegiNext");
            window.Show();
            window.UpdateLayout();
            var point = button.TranslatePoint(new(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(button.IsFocused);
            clickCount = 0;
            var checkedBefore = (button as ToggleButton)?.IsChecked;
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal(1, playCount);
            Assert.Equal(0, clickCount);
            Assert.Equal(checkedBefore, (button as ToggleButton)?.IsChecked);
            UiTestActions.Press(window, Key.Space);
            Assert.Equal(2, playCount);
            Assert.Equal(0, clickCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void KeyReleaseRemainsConsumedAfterCommandAvailabilityAndPreferencesChange()
    {
        using var environment = new UiTestEnvironment();
        var available = true;
        var playCount = 0;
        var clickCount = 0;
        var commands = CreateCommands();
        commands[WorkbenchCommand.PLAY_PAUSE] = new(() =>
        {
            playCount++;
            available = false;
        }, () => available);
        using var registry = new WorkbenchWindowRegistry(new(id => commands[id]), () => { });
        var button = new Button { Content = "Action" };
        button.Click += (_, _) => clickCount++;
        var window = new Window { Width = 600, Height = 360, Content = button };
        try
        {
            registry.Register(window, () => "AegiNext");
            window.Show();
            button.Focus();
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            registry.UpdatePreferences(new());
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal(1, playCount);
            Assert.Equal(0, clickCount);
            UiTestActions.Press(window, Key.Space);
            Assert.Equal(1, playCount);
            Assert.Equal(0, clickCount);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void TextInputAndOpenComboPopupRetainLocalSpaceBehavior()
    {
        using var environment = new UiTestEnvironment();
        var playCount = 0;
        var commands = CreateCommands();
        commands[WorkbenchCommand.PLAY_PAUSE] = new(() => playCount++, () => true);
        using var registry = new WorkbenchWindowRegistry(new(id => commands[id]), () => { });
        var text = new TextBox { Text = "Subtitle" };
        var combo = new ComboBox { ItemsSource = new[] { "First", "Second" }, SelectedIndex = 0 };
        var window = new Window
        {
            Width = 600, Height = 360, Content = new StackPanel { Children = { text, combo } }
        };
        try
        {
            registry.Register(window, () => "AegiNext");
            window.Show();
            text.Focus();
            text.CaretIndex = text.Text.Length;
            window.KeyPress(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyTextInput(" ");
            window.KeyRelease(Key.Space, RawInputModifiers.None, PhysicalKey.None, null);
            Assert.Equal("Subtitle ", text.Text);
            Assert.Equal(0, playCount);
            combo.Focus();
            combo.IsDropDownOpen = true;
            Dispatcher.UIThread.RunJobs();
            UiTestActions.Press(window, Key.Space);
            Assert.Equal(0, playCount);
        }
        finally
        {
            combo.IsDropDownOpen = false;
            window.Close();
        }
    }

    [AvaloniaFact]
    public void ModalDialogSpaceActivatesItsFocusedButtonWithoutPlayback()
    {
        using var environment = new UiTestEnvironment();
        var playCount = 0;
        var clickCount = 0;
        var commands = CreateCommands();
        commands[WorkbenchCommand.PLAY_PAUSE] = new(() => playCount++, () => true);
        using var registry = new WorkbenchWindowRegistry(new(id => commands[id]), () => { });
        var main = new Window { Width = 600, Height = 360 };
        var button = new Button { Content = "Confirm" };
        button.Click += (_, _) => clickCount++;
        var dialog = new Window { Width = 400, Height = 240, Content = button };
        try
        {
            registry.Register(main, () => "AegiNext");
            registry.Register(dialog, () => "Confirm");
            main.Show();
            _ = dialog.ShowDialog(main);
            button.Focus();
            Assert.True(dialog.IsDialog);
            UiTestActions.Press(dialog, Key.Space);
            Assert.Equal(1, clickCount);
            Assert.Equal(0, playCount);
        }
        finally
        {
            dialog.Close();
            main.Close();
        }
    }

    private static Dictionary<WorkbenchCommand, WorkbenchCommandAdapter> CreateCommands()
    {
        return Enum.GetValues<WorkbenchCommand>().ToDictionary(id => id,
            _ => new WorkbenchCommandAdapter(() => { }, () => true));
    }
}
