using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.ColorTags;
using AegiNext.Desktop.Shortcuts;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleColorTagUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TagBackgroundUpdatesInPlaceAndUndoRestoresItInBothThemes(bool dark)
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        context.Window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        Flush(context.Window);
        var originalRow = context.ViewModel.Subtitles.Rows[0];
        var before = Assert.IsAssignableFrom<ISolidColorBrush>(RowSurface(context.Window, originalRow.Id).Background)
            .Color;

        await context.Session.SetSubtitleColorTagAsync([originalRow.Id], document.ColorTags[1].Id);
        Flush(context.Window);

        Assert.Same(originalRow, context.ViewModel.Subtitles.Rows[0]);
        var after = Assert.IsAssignableFrom<ISolidColorBrush>(RowSurface(context.Window, originalRow.Id).Background)
            .Color;
        Assert.NotEqual(before, after);
        Assert.True(after.B > after.R);
        Assert.All(RowSurface(context.Window, originalRow.Id).GetVisualDescendants().OfType<TextBox>(),
            input => Assert.Equal(Colors.Transparent,
                Assert.IsAssignableFrom<ISolidColorBrush>(input.Background).Color));
        Assert.True(context.Session.Editor.Undo());
        Flush(context.Window);
        Assert.Equal(before,
            Assert.IsAssignableFrom<ISolidColorBrush>(RowSurface(context.Window, originalRow.Id).Background).Color);
    }

    [AvaloniaFact]
    public async Task FilterComboPrunesHiddenSelectionAndLanguageRefreshPreservesTheFilter()
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        context.Session.SelectSubtitleRows(document.Subtitles[0].Id, document.Subtitles.Select(line => line.Id));
        var combo = UiTestActions.Find<ComboBox>(context.Window, "SubtitleColorTagFilterCombo");
        combo.SelectedItem =
            context.ViewModel.Subtitles.ColorTagFilters.Single(choice =>
                choice.Value.TagId == document.ColorTags[0].Id);
        Flush(context.Window);

        Assert.Equal(2, UiTestActions.Find<ListBox>(context.Window, "SubtitleList").ItemCount);
        Assert.Equal(2, context.Session.SelectedSubtitleIds.Count);
        Assert.DoesNotContain(document.Layers[1].Id, context.ViewModel.Effects.SelectedIds);
        Localization.SetLanguage("zh-CN");
        Flush(context.Window);
        Assert.Equal(document.ColorTags[0].Id,
            Assert.IsType<SubtitleColorTagFilterChoice>(combo.SelectedItem).Value.TagId);
        Assert.Equal("外来标签", Assert.IsType<SubtitleColorTagFilterChoice>(combo.SelectedItem).Name);
        Assert.Contains(Localization.Get("Workbench.ColorTag.Filter"), Assert.IsType<string>(ToolTip.GetTip(combo)),
            StringComparison.Ordinal);
        Assert.Contains("外来标签", Assert.IsType<string>(ToolTip.GetTip(combo)), StringComparison.Ordinal);
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task EnterSkipsHiddenRowsAndRemainsAtTheLastFilteredInput()
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        Assert.True(context.Session.TrySelectSubtitleColorTagFilter(new(false, document.ColorTags[0].Id)));
        Flush(context.Window);
        var first = RowText(context.Window, document.Subtitles[0].Id);
        Assert.True(first.Focus());

        UiTestActions.Press(context.Window, Key.Enter);
        Flush(context.Window);

        Assert.True(RowText(context.Window, document.Subtitles[2].Id).IsFocused);
        UiTestActions.Press(context.Window, Key.Enter);
        Flush(context.Window);
        Assert.True(RowText(context.Window, document.Subtitles[2].Id).IsFocused);
        Assert.Equal(3, context.Session.DocumentSnapshot.Subtitles.Length);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task DisabledListMergeShortcutDoesNotFallBackToHiddenAdjacentSubtitle()
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        context.Session.SelectCue(document.Subtitles[0].Id);
        Assert.True(context.Session.TrySelectSubtitleColorTagFilter(new(false, document.ColorTags[0].Id)));
        Flush(context.Window);
        Assert.True(RowText(context.Window, document.Subtitles[0].Id).Focus());
        Assert.False(UiTestActions.Find<Button>(context.Window, "MergeCueButton").IsEffectivelyEnabled);

        UiTestActions.Press(context.Window, Key.M, CommandModifier() | RawInputModifiers.Shift);
        Flush(context.Window);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.MERGE_SUBTITLE);
        Flush(context.Window);

        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComposingSubtitleInputKeepsMergeOwnedAndPreventsGlobalFallback(bool filtered)
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        context.Session.SelectCue(document.Subtitles[0].Id);
        if (filtered)
        {
            Assert.True(context.Session.TrySelectSubtitleColorTagFilter(new(false, document.ColorTags[0].Id)));
        }

        Flush(context.Window);
        var input = RowText(context.Window, document.Subtitles[0].Id);
        Assert.True(input.Focus());
        var presenter = input.GetVisualDescendants().OfType<TextPresenter>().Single();
        presenter.PreeditText = "字幕候选";

        Assert.False(context.Window.GetCommand(WorkbenchCommand.MERGE_SUBTITLE).CanExecute(null));
        UiTestActions.Press(context.Window, Key.M, CommandModifier() | RawInputModifiers.Shift);
        await context.ViewModel.ExecuteCommandAsync(WorkbenchCommand.MERGE_SUBTITLE);
        Flush(context.Window);

        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.Equal("字幕候选", presenter.PreeditText);
        presenter.PreeditText = null;
    }

    [AvaloniaFact]
    public async Task MergeMenuAvailabilityRefreshesWhenOnlyPanelFocusChanges()
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        context.Session.UpdatePreferences(context.Session.Preferences with { WindowMenuOnMac = true });
        context.Session.SelectCue(document.Subtitles[0].Id);
        Assert.True(context.Session.TrySelectSubtitleColorTagFilter(new(false, document.ColorTags[0].Id)));
        var command = context.Window.GetCommand(WorkbenchCommand.MERGE_SUBTITLE);
        var (group, merge) = MergeMenu(context.Window, command);
        var timeline = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        var input = RowText(context.Window, document.Subtitles[0].Id);
        var notifications = 0;
        command.CanExecuteChanged += (_, _) => notifications++;

        Assert.True(input.Focus());
        Flush(context.Window);
        Assert.False(command.CanExecute(null));
        group.IsSubMenuOpen = true;
        Flush(context.Window);
        Assert.False(merge.IsEffectivelyEnabled);
        group.IsSubMenuOpen = false;

        var before = notifications;
        Assert.True(timeline.Focus());
        Flush(context.Window);
        Assert.True(notifications > before);
        Assert.True(command.CanExecute(null));
        group.IsSubMenuOpen = true;
        Flush(context.Window);
        Assert.True(merge.IsEffectivelyEnabled);
        group.IsSubMenuOpen = false;

        before = notifications;
        Assert.True(input.Focus());
        Flush(context.Window);
        Assert.True(notifications > before);
        Assert.False(command.CanExecute(null));
        group.IsSubMenuOpen = true;
        Flush(context.Window);
        Assert.False(merge.IsEffectivelyEnabled);
        group.IsSubMenuOpen = false;
        Assert.Same(document, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpenSubtitleMenuRetainsTheVisibleListMergeScope(bool multiple)
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        if (multiple)
        {
            document = document with
            {
                Subtitles = document.Subtitles
                    .SetItem(1, document.Subtitles[1] with { ColorTagId = document.ColorTags[0].Id })
                    .SetItem(2, document.Subtitles[2] with { ColorTagId = document.ColorTags[1].Id })
            };
            context.Session.Editor.Reset(document);
        }

        context.Session.UpdatePreferences(context.Session.Preferences with { WindowMenuOnMac = true });
        context.Session.SelectCue(document.Subtitles[0].Id);
        Assert.True(context.Session.TrySelectSubtitleColorTagFilter(new(false, document.ColorTags[0].Id)));
        if (multiple)
        {
            context.Session.SelectSubtitleRows(document.Subtitles[0].Id,
                [document.Subtitles[0].Id, document.Subtitles[1].Id]);
        }

        Flush(context.Window);
        Assert.True(RowText(context.Window, document.Subtitles[0].Id).Focus());
        var command = context.Window.GetCommand(WorkbenchCommand.MERGE_SUBTITLE);
        var (group, merge) = MergeMenu(context.Window, command);
        UiTestActions.ClickFontMenuItem(group);
        Assert.True(group.IsSubMenuOpen);
        Assert.True(group.Focus());
        Flush(context.Window);

        Assert.Equal(multiple, command.CanExecute(null));
        Assert.Equal(multiple, merge.IsEffectivelyEnabled);
        UiTestActions.ClickFontMenuItem(merge);
        await (Assert.IsAssignableFrom<IAsyncRelayCommand>(command).ExecutionTask ?? Task.CompletedTask);
        Flush(context.Window);

        if (multiple)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (ReferenceEquals(document, context.Session.DocumentSnapshot))
            {
                Assert.True(DateTime.UtcNow < deadline, context.DescribeTaskState());
                await Task.Delay(5, TestContext.Current.CancellationToken);
                Flush(context.Window);
            }

            Assert.Equal(2, context.Session.DocumentSnapshot.Subtitles.Length);
            Assert.Same(document.Subtitles[2], context.Session.DocumentSnapshot.Subtitles[1]);
            Assert.True(context.Session.Editor.Undo());
        }

        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RightClickTagMenuFreezesTargetsAndMarksTheWholeSelection(bool timeline)
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context, timeline);
        context.Session.SelectSubtitleRows(document.Subtitles[0].Id,
            document.Subtitles.Take(2).Select(line => line.Id));
        Flush(context.Window);
        var menu = OpenMenu(context, document, timeline);
        var tag = FindTag(menu, document.ColorTags[1].Id);
        var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(tag.Command);
        context.Session.SelectSubtitleRows(document.Subtitles[2].Id, [document.Subtitles[2].Id]);
        menu.Close();

        await command.ExecuteAsync(null);
        Flush(context.Window);

        Assert.Equal(document.ColorTags[1].Id, context.Session.DocumentSnapshot.Subtitles[0].ColorTagId);
        Assert.Equal(document.ColorTags[1].Id, context.Session.DocumentSnapshot.Subtitles[1].ColorTagId);
        Assert.Same(document.Subtitles[2], context.Session.DocumentSnapshot.Subtitles[2]);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task ProjectTagsRemainAvailableWithoutMatchingPersonalLibraryEntries()
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        await context.Session.ApplicationContext.Initialization;
        context.Session.SelectCue(document.Subtitles[0].Id);
        var menu = OpenMenu(context, document, false);
        var projectItem = FindTag(menu, document.ColorTags[0].Id);
        Assert.Equal("外来标签", projectItem.Header);
        Assert.True(projectItem.IsChecked);
        Assert.NotNull(projectItem.Icon);
        Assert.False(
            context.Session.ApplicationContext.ColorTagLibrary.Snapshot.Tags.Any(tag =>
                tag.Id == document.ColorTags[0].Id));
        menu.Close();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ManageTagsFromTheListAndTimelineOpensTheDedicatedSettingsPage(bool timeline)
    {
        await using var context = new MainWindowTestContext();
        await context.Session.ApplicationContext.Initialization;
        var document = Prepare(context, timeline);
        context.Session.SelectCue(document.Subtitles[0].Id);
        var menu = OpenMenu(context, document, timeline);
        var group = menu.Items.OfType<MenuItem>().Single(item => item.Items.OfType<MenuItem>()
            .Any(child => child.Name == "ManageSubtitleColorTagsMenuItem"));
        UiTestActions.ClickFontMenuItem(group);
        Flush(context.Window);
        Assert.True(group.IsSubMenuOpen);
        var manage = group.Items.OfType<MenuItem>().Single(item => item.Name == "ManageSubtitleColorTagsMenuItem");
        var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(manage.Command);
        UiTestActions.ClickFontMenuItem(manage);
        await (command.ExecutionTask ?? Task.CompletedTask);
        var settings = Assert.Single(context.Window.OwnedWindows.OfType<SettingsWindow>());
        try
        {
            Flush(settings);
            Assert.Equal(SettingsPage.SUBTITLE_COLOR_TAGS, settings.CurrentPage);
            Assert.True(UiTestActions.Find<SubtitleColorTagsSettingsView>(settings, "ColorTagsView")
                .IsEffectivelyVisible);
            Assert.Equal(Localization.Get("Settings.SubtitleColorTags"), settings.ViewModel.PageTitle);
            Assert.Same(document, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            settings.Close();
            await settings.CloseCompletion;
        }
    }

    private static ProjectDocument Prepare(MainWindowTestContext context, bool timeline = false)
    {
        var red = new SubtitleColorTag { Name = "外来标签", ColorHex = "#FF3B30" };
        var blue = new SubtitleColorTag { Name = "已处理", ColorHex = "#007AFF" };
        var lines = Enumerable.Range(0, 3).Select(index => new SubtitleLine
        {
            Start = new(index * 3 + 1),
            End = new(index * 3 + 2),
            Text = "Line " + index,
            ColorTagId = index == 1 ? blue.Id : red.Id
        }).ToImmutableArray();
        var document = new ProjectDocument
        {
            ColorTags = [red, blue],
            Subtitles = lines,
            Layers = lines.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            }).ToImmutableArray()
        };
        context.Session.Editor.Reset(document);
        context.Window.Layouts.Activate(timeline ? "timeline" : "subtitles");
        Flush(context.Window);
        return document;
    }

    private static Grid RowSurface(Window window, Guid id) => UiTestActions.Find<ListBox>(window, "SubtitleList")
        .GetVisualDescendants().OfType<Grid>().Single(grid =>
            grid.Name == "SubtitleRowSurface" && grid.DataContext is SubtitleRow row && row.Id == id);

    private static TextBox RowText(Window window, Guid id) => RowSurface(window, id).GetVisualDescendants()
        .OfType<TextBox>().Single(input => input.AcceptsReturn);

    private static ContextMenu OpenMenu(MainWindowTestContext context, ProjectDocument document, bool timeline)
    {
        if (timeline)
        {
            var control = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
            context.ViewModel.Timeline.IsSnapEnabled = false;
            control.ViewStart = 0;
            Flush(context.Window);
            var point = control.TranslatePoint(control.GetClipRectangle(document.Layers[0].Id)!.Value.Center,
                context.Window)!.Value;
            context.Window.MouseDown(point, MouseButton.Right);
            context.Window.MouseUp(point, MouseButton.Right);
            Flush(context.Window);
            return Assert.Single(control.GetVisualAncestors().OfType<TimelinePanelView>()).ClipMenu;
        }

        var surface = RowSurface(context.Window, document.Subtitles[0].Id);
        var location = surface.TranslatePoint(new Point(10, surface.Bounds.Height / 2), context.Window)!.Value;
        context.Window.MouseDown(location, MouseButton.Right);
        context.Window.MouseUp(location, MouseButton.Right);
        Flush(context.Window);
        return UiTestActions.Find<ListBox>(context.Window, "SubtitleList").ContextMenu!;
    }

    private static MenuItem FindTag(ContextMenu menu, Guid id) => menu.Items.OfType<MenuItem>()
        .SelectMany(item => item.Items.OfType<MenuItem>())
        .Single(item => item.Name == "SubtitleColorTagProject" + id.ToString("N"));

    private static (MenuItem Group, MenuItem Merge) MergeMenu(Window window, System.Windows.Input.ICommand command)
    {
        Flush(window);
        var menu = window.GetVisualDescendants().OfType<Menu>().Single(value => value.Name == "MainMenu");
        var group = menu.Items.OfType<MenuItem>().Single(item => item.Items.OfType<MenuItem>()
            .Any(leaf => ReferenceEquals(leaf.Command, command)));
        return (group, group.Items.OfType<MenuItem>().Single(item => ReferenceEquals(item.Command, command)));
    }

    private static RawInputModifiers CommandModifier() =>
        OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
