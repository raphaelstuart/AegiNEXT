using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Layouts;
using AegiNext.Desktop.Shortcuts;
using AegiNext.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleSelectionAndMergeUiTests
{
    private static readonly string[] subtitleTexts = ["First", "Second", "Third"];

    [AvaloniaFact]
    public async Task ActualRowClicksSupportToggleAndShiftRangeAndSurviveRefresh()
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        var ids = document.Subtitles.Select(line => line.Id).ToArray();
        var window = context.Window;
        Click(window, RowHeader(window, ids[0]));
        Click(window, RowHeader(window, ids[1]), ToggleModifier());
        AssertSelection(context, ids[..2]);
        Assert.Equal(ids[1], context.Session.SelectedCueId);
        context.Session.RefreshDocument();
        Flush(window);
        AssertSelection(context, ids[..2]);

        Click(window, RowHeader(window, ids[0]));
        Click(window, RowHeader(window, ids[2]), RawInputModifiers.Shift);
        AssertSelection(context, ids);
        Assert.False(context.Session.Editor.CanUndo);

        Click(window, RowHeader(window, ids[1]), ToggleModifier());
        AssertSelection(context, [ids[0], ids[2]]);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task TextEditingActivatesItsRowWithoutDiscardingTheSelectedRange()
    {
        await using var context = new MainWindowTestContext();
        var document = Prepare(context);
        var ids = document.Subtitles.Select(line => line.Id).ToArray();
        var window = context.Window;
        Click(window, RowHeader(window, ids[0]));
        Click(window, RowHeader(window, ids[2]), RawInputModifiers.Shift);
        var text = RowInput(window, ids[1], 4);
        Click(window, text);
        Assert.True(text.IsFocused);
        Assert.Equal(ids[1], context.Session.SelectedCueId);
        AssertSelection(context, ids);
        text.SelectAll();
        window.KeyTextInput("Second edited 中文");
        Flush(window);

        Assert.Equal("Second edited 中文", context.ViewModel.Subtitles.Rows[1].Text);
        Assert.True(context.ViewModel.TryCommitDrafts());
        Assert.Equal("Second edited 中文", context.Session.DocumentSnapshot.Subtitles[1].Text);
        AssertSelection(context, ids);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(document, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task ActualMergeButtonMergesThreeSelectedRowsAndUndoRedoRestoresTheWholeTransaction()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context);
        var ids = original.Subtitles.Select(line => line.Id).ToArray();
        var window = context.Window;
        Click(window, RowHeader(window, ids[0]));
        Click(window, RowHeader(window, ids[2]), RawInputModifiers.Shift);
        UiTestActions.Click(window, "MergeCueButton");
        Flush(window);

        var merged = context.Session.DocumentSnapshot;
        Assert.Equal("First\nSecond\nThird", Assert.Single(merged.Subtitles).Text);
        Assert.Equal(original.Subtitles[0].Style, merged.Subtitles[0].Style);
        Assert.Equal(ids[0], Assert.Single(merged.Layers).SubtitleId);
        AssertSelection(context, [ids[0]]);
        Assert.False(UiTestActions.Find<Button>(window, "MergeCueButton").IsEffectivelyEnabled);
        window.GetCommand(WorkbenchCommand.UNDO).Execute(null);
        Flush(window);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        window.GetCommand(WorkbenchCommand.REDO).Execute(null);
        Flush(window);
        Assert.Same(merged, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData(true, 1)]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    [InlineData(false, 2)]
    [InlineData(true, 3)]
    [InlineData(false, 3)]
    public async Task DeleteButtonAndShortcutRemoveSelectedRowsInOneUndoableTransaction(bool useButton, int selectionCount)
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context);
        var ids = original.Subtitles.Select(line => line.Id).ToArray();
        var window = context.Window;
        Click(window, RowHeader(window, ids[0]));
        if (selectionCount > 1)
        {
            Click(window, RowHeader(window, ids[2]), selectionCount == 3 ? RawInputModifiers.Shift : ToggleModifier());
        }
        var selectedIds = selectionCount switch
        {
            1 => ids[..1],
            2 => [ids[0], ids[2]],
            _ => ids
        };
        AssertSelection(context, selectedIds);

        if (useButton)
        {
            UiTestActions.Click(window, "DeleteCueButton");
        }
        else
        {
            UiTestActions.Press(window, Key.Delete);
        }
        Flush(window);

        var deleted = context.Session.DocumentSnapshot;
        Assert.Equal(original.Subtitles.Where(line => !selectedIds.Contains(line.Id)), deleted.Subtitles);
        Assert.Equal(original.Layers.Where(layer => !selectedIds.Contains(layer.SubtitleId!.Value)), deleted.Layers);
        Assert.Null(context.Session.LastError);
        if (selectionCount == 3)
        {
            AssertSelection(context, []);
            Assert.False(UiTestActions.Find<Button>(window, "DeleteCueButton").IsEffectivelyEnabled);
        }
        window.GetCommand(WorkbenchCommand.UNDO).Execute(null);
        Flush(window);
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        window.GetCommand(WorkbenchCommand.REDO).Execute(null);
        Flush(window);
        Assert.Same(deleted, context.Session.DocumentSnapshot);
    }

    [AvaloniaFact]
    public async Task DeleteButtonUsesTheWholeSelectionAfterAnInputChangesThePrimaryRow()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context);
        var ids = original.Subtitles.Select(line => line.Id).ToArray();
        var window = context.Window;
        Click(window, RowHeader(window, ids[0]));
        Click(window, RowHeader(window, ids[2]), RawInputModifiers.Shift);
        Click(window, RowInput(window, ids[1], 4));
        Assert.Equal(ids[1], context.Session.SelectedCueId);
        AssertSelection(context, ids);

        UiTestActions.Click(window, "DeleteCueButton");
        Flush(window);

        Assert.Empty(context.Session.DocumentSnapshot.Subtitles);
        Assert.Empty(context.Session.DocumentSnapshot.Layers);
        Assert.True(context.Session.Editor.Undo());
        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
    }

    [AvaloniaFact]
    public async Task InvalidTimeDraftBlocksBatchDeletionWithoutPartialChanges()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context);
        var ids = original.Subtitles.Select(line => line.Id).ToArray();
        var window = context.Window;
        Click(window, RowHeader(window, ids[0]));
        Click(window, RowHeader(window, ids[2]), RawInputModifiers.Shift);
        var input = RowInput(window, ids[1], 1);
        Click(window, input);
        input.SelectAll();
        window.KeyTextInput("invalid");
        Flush(window);
        try
        {
            UiTestActions.Click(window, "DeleteCueButton");
            Flush(window);

            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            AssertSelection(context, ids);
            Assert.Equal("invalid", RowInput(window, ids[1], 1).Text);
            Assert.True(RowInput(window, ids[1], 1).IsFocused);
        }
        finally
        {
            context.ViewModel.Subtitles.Rows[1].Accept(original.Subtitles[1]);
        }
    }

    [AvaloniaFact]
    public async Task InvalidTimeDraftRejectsActualSelectionChangeAndPreservesInputFocus()
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context);
        var ids = original.Subtitles.Select(line => line.Id).ToArray();
        var window = context.Window;
        Click(window, RowHeader(window, ids[0]));
        Click(window, RowHeader(window, ids[1]), ToggleModifier());
        var input = RowInput(window, ids[0], 1);
        Click(window, input);
        Assert.True(input.IsFocused);
        input.SelectAll();
        window.KeyTextInput("invalid");
        Flush(window);
        try
        {
            Click(window, RowHeader(window, ids[2]));
            AssertSelection(context, ids[..2]);
            var invalidInput = RowInput(window, ids[0], 1);
            Assert.Equal("invalid", context.ViewModel.Subtitles.Rows[0].StartText);
            Assert.Equal("invalid", invalidInput.Text);
            var focused = window.FocusManager?.GetFocusedElement() as Control;
            Assert.True(invalidInput.IsFocused, $"Focused: {focused?.GetType().Name}, row: {(focused?.DataContext as SubtitleRow)?.Id}, invalid: {ids[0]}");
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            context.ViewModel.Subtitles.Rows[0].Accept(original.Subtitles[0]);
        }
    }

    [AvaloniaFact]
    public async Task IncompatibleThirdEffectRejectsActualBatchMergeWithoutPartialChanges()
    {
        await using var context = new MainWindowTestContext();
        var fixture = Prepare(context);
        var original = fixture with { Layers = fixture.Layers.SetItem(2, fixture.Layers[2] with { Opacity = 0.5 }) };
        context.Session.Editor.Reset(original);
        Flush(context.Window);
        var ids = original.Subtitles.Select(line => line.Id).ToArray();
        Click(context.Window, RowHeader(context.Window, ids[0]));
        Click(context.Window, RowHeader(context.Window, ids[2]), RawInputModifiers.Shift);
        UiTestActions.Click(context.Window, "MergeCueButton");
        Flush(context.Window);

        Assert.Same(original, context.Session.DocumentSnapshot);
        Assert.False(context.Session.Editor.CanUndo);
        Assert.NotNull(context.Session.LastError);
        AssertSelection(context, ids);
    }

    private static ProjectDocument Prepare(MainWindowTestContext context)
    {
        var lines = subtitleTexts.Select((text, index) => new SubtitleLine
        {
            Text = text, Start = new(index * 3), End = new(index * 3 + 2),
            Style = new() { FontFamily = "sans-serif", FontSize = 24 }
        }).ToImmutableArray();
        var document = new ProjectDocument
        {
            Subtitles = lines,
            Layers = lines.Select(line => new ProjectLayer
            {
                Kind = LayerKind.SUBTITLE, SubtitleId = line.Id, Start = line.Start, End = line.End
            }).ToImmutableArray()
        };
        context.Session.Editor.Reset(document);
        context.Window.Layouts.Activate(WorkbenchPanelIds.SUBTITLES);
        Flush(context.Window);
        return document;
    }

    private static TextBlock RowHeader(MainWindow window, Guid id)
    {
        var list = UiTestActions.Find<ListBox>(window, "SubtitleList");
        var row = list.Items.OfType<SubtitleRow>().Single(value => value.Id == id);
        list.ScrollIntoView(row);
        Flush(window);
        return list.GetVisualDescendants().OfType<TextBlock>().Single(control =>
            control.DataContext is SubtitleRow row && row.Id == id && Grid.GetColumn(control) == 0 &&
            control.Text == row.ContentType && control.GetVisualParent() is Grid { ColumnDefinitions.Count: 5 });
    }

    private static TextBox RowInput(MainWindow window, Guid id, int column)
    {
        var list = UiTestActions.Find<ListBox>(window, "SubtitleList");
        var row = list.Items.OfType<SubtitleRow>().Single(value => value.Id == id);
        list.ScrollIntoView(row);
        Flush(window);
        return list.GetVisualDescendants().OfType<TextBox>().Single(control =>
            control.DataContext is SubtitleRow row && row.Id == id && Grid.GetColumn(control) == column);
    }

    private static void AssertSelection(MainWindowTestContext context, Guid[] expected)
    {
        Assert.Equal(expected.Order(), context.Session.SelectedSubtitleIds.Order());
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        Assert.Equal(expected.Order(), list.Selection.SelectedItems.OfType<SubtitleRow>().Select(row => row.Id).Order());
    }

    private static RawInputModifiers ToggleModifier()
    {
        var modifier = Avalonia.Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers;
        return modifier.HasFlag(KeyModifiers.Meta) ? RawInputModifiers.Meta : RawInputModifiers.Control;
    }

    private static void Click(MainWindow window, Control control, RawInputModifiers modifiers = RawInputModifiers.None)
    {
        control.BringIntoView();
        Flush(window);
        using var frame = window.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
        var hit = Assert.IsAssignableFrom<Visual>(window.InputHitTest(point));
        var target = control is TextBlock { DataContext: SubtitleRow }
            ? control.GetVisualAncestors().OfType<ListBoxItem>().Single()
            : control;
        Assert.True(ReferenceEquals(hit, target) || hit.GetVisualAncestors().Contains(target));
        window.MouseDown(point, MouseButton.Left, modifiers);
        window.MouseUp(point, MouseButton.Left, modifiers);
        Flush(window);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
