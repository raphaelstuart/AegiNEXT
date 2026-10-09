using AegiNext.Core.Effects;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Workspace;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AegiNext.Desktop.Views;
using AegiNext.Core.Editing;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Shortcuts;

namespace AegiNext.Desktop.Ui.Tests;

internal static class UiTestActions
{
    internal static Guid CreateSubtitle(MainWindowTestContext context, MediaTime? duration = null, string text = "Subtitle ABC 中文 123")
    {
        var before = context.Session.DocumentSnapshot.Subtitles.Select(line => line.Id).ToHashSet();
        Click(context.Window, "AddCueButton");
        var cue = Assert.Single(context.Session.DocumentSnapshot.Subtitles, line => !before.Contains(line.Id));
        context.Session.Editor.SetSubtitleTiming(cue.Id, cue.Start, cue.Start + (duration ?? new MediaTime(5)), TimelineEditMode.CROP);
        context.Session.Editor.UpdateSubtitle(cue.Id, line => line with { Text = text });
        context.Session.SelectCue(cue.Id);
        context.Window.GetCommand(WorkbenchCommand.VIEW_EFFECTS).Execute(null);
        Dispatcher.UIThread.RunJobs();
        context.Window.UpdateLayout();
        return cue.Id;
    }

    internal static T Find<T>(Control root, string name) where T : Control
    {
        if (root is Window host)
        {
            host.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            host.UpdateLayout();
        }
        var control = (root.Name == name ? root as T : null) ?? root.GetLogicalDescendants().OfType<T>().FirstOrDefault(control => control.Name == name)
            ?? root.GetVisualDescendants().OfType<T>().FirstOrDefault(control => control.Name == name);
        if (control is not null && (root is not MainWindow || control.IsAttachedToVisualTree()))
        {
            return control;
        }
        if (root is MainWindow window)
        {
            foreach (var pair in window.Panels)
            {
                control = pair.Value.GetLogicalDescendants().OfType<T>().FirstOrDefault(candidate => candidate.Name == name)
                    ?? pair.Value.GetVisualDescendants().OfType<T>().FirstOrDefault(candidate => candidate.Name == name);
                if (control is not null)
                {
                    window.Layouts.Activate(pair.Key);
                    window.UpdateLayout();
                    Dispatcher.UIThread.RunJobs();
                    window.UpdateLayout();
                    return control;
                }
            }
        }
        throw new InvalidOperationException($"Missing control {name}.");
    }

    internal static void SelectBuiltinPreset(Window window, string scriptId)
    {
        var index = BuiltinEffectScripts.Templates.Select((template, index) => (template, index))
            .Single(value => value.template.Script.Id == scriptId).index;
        Find<ComboBox>(window, "PresetCombo").SelectedIndex = index;
        Dispatcher.UIThread.RunJobs();
    }

    internal static void SelectLanguage(Window window, string languageID)
    {
        var selector = Find<ComboBox>(window, "LanguageCombo");
        selector.SelectedItem = selector.Items.OfType<LanguageInfo>()
            .Single(language => string.Equals(language.LanguageID, languageID, StringComparison.OrdinalIgnoreCase));
        Dispatcher.UIThread.RunJobs();
    }

    internal static void SelectSettingsPage(Window window, SettingsPage page)
    {
        var navigation = Find<ListBox>(window, "Navigation");
        var item = navigation.Items.Cast<ListBoxItem>().Single(value => Equals(value.DataContext, page));
        item.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var point = item.TranslatePoint(new(item.Bounds.Width / 2, item.Bounds.Height / 2), window)!.Value;
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    internal static void SelectShortcut(Window window, WorkbenchCommand command)
    {
        var list = Find<ListBox>(window, "ShortcutList");
        list.SelectedItem = list.Items.OfType<ShortcutSettingsListItem>().Single(item => item.Row?.Command == command);
        Dispatcher.UIThread.RunJobs();
    }

    internal static void SelectAnimationProperty(Window window, AnimationProperty property)
    {
        var selector = Find<ComboBox>(window, "PropertyCombo");
        selector.SelectedItem = selector.Items.OfType<AnimationPropertyChoice>().Single(value => value.Property == property);
        Dispatcher.UIThread.RunJobs();
    }

    internal static void Click(Window window, string name)
    {
        var button = Find<Button>(window, name);
        Assert.True(button.IsEffectivelyEnabled);
        button.BringIntoView();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)
            ?? throw new InvalidOperationException($"Button {name} is not attached to the window.");
        window.MouseDown(point, MouseButton.Left);
        window.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    internal static void Press(Window window, Key key, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        window.KeyPress(key, modifiers, PhysicalKey.None, null);
        window.KeyRelease(key, modifiers, PhysicalKey.None, null);
    }

    internal static void ClickFontMenuItem(MenuItem item)
    {
        item.BringIntoView();
        Dispatcher.UIThread.RunJobs();
        var root = Assert.IsAssignableFrom<TopLevel>(TopLevel.GetTopLevel(item));
        root.UpdateLayout();
        var point = item.TranslatePoint(new(item.Bounds.Width / 2, item.Bounds.Height / 2), root)!.Value;
        root.MouseMove(point);
        root.MouseDown(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        root.MouseUp(point, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    internal static void SetText(TextBox input, string text)
    {
        input.Text = text;
        Dispatcher.UIThread.RunJobs();
    }
}
