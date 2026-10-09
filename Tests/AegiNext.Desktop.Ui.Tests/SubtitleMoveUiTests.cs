using AegiNext.Core.Projects;
using AegiNext.Core.Timing;
using AegiNext.Desktop.Controls;
using AegiNext.Desktop.Editing;
using AegiNext.Desktop.Panels.Timeline;
using AegiNext.Desktop.Views;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.Input;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class SubtitleMoveUiTests
{
    [AvaloniaTheory]
    [InlineData(false, 125)]
    [InlineData(false, -125)]
    [InlineData(true, 125)]
    [InlineData(true, -125)]
    public async Task SelectedItemContextMovesTheWholeSelectionByExactMilliseconds(bool timeline, int milliseconds)
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context, timeline);
        var selected = SelectTargets(context, original, timeline);
        var menu = OpenMenu(context, original, timeline, 1);
        Assert.True(menu.IsOpen);
        AssertSelection(context, selected, timeline);
        var operation = ExecuteMove(menu, timeline);
        menu.Close();
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<IntegerInputDialog>());
        try
        {
            Assert.True(dialog.IsDialog);
            Assert.Same(context.Window, dialog.Owner);
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            SetIntegerText(dialog, milliseconds > 0 ? "+125" : "-125");
            UiTestActions.Click(dialog, "ConfirmButton");
            await operation;
            Flush(context.Window);

            Assert.False(dialog.IsVisible);
            Assert.Empty(context.Window.OwnedWindows.OfType<IntegerInputDialog>());
            Assert.Null(context.Session.LastError);
            var moved = context.Session.DocumentSnapshot;
            var offset = new MediaTime(milliseconds, 1000);
            foreach (var cue in original.Subtitles.Take(2))
            {
                Assert.Equal(cue with { Start = cue.Start + offset, End = cue.End + offset },
                    moved.Subtitles.Single(value => value.Id == cue.Id));
            }
            var layerIds = original.Layers.Take(2).Select(layer => layer.Id).ToHashSet();
            if (timeline)
            {
                layerIds.Add(original.Layers[^1].Id);
            }
            foreach (var layer in original.Layers)
            {
                var actual = moved.Layers.Single(value => value.Id == layer.Id);
                if (layerIds.Contains(layer.Id))
                {
                    Assert.Equal(layer with { Start = layer.Start + offset, End = layer.End + offset }, actual);
                }
                else
                {
                    Assert.Same(layer, actual);
                }
            }
            Assert.Same(original.Subtitles[2], moved.Subtitles[2]);
            AssertSelection(context, selected, timeline);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.True(context.Session.Editor.Redo());
            Assert.Same(moved, context.Session.DocumentSnapshot);
        }
        finally
        {
            dialog.Close((int?)null);
            await operation;
        }
    }

    [AvaloniaTheory]
    [InlineData(false, "CancelButton")]
    [InlineData(false, "Escape")]
    [InlineData(false, "Close")]
    [InlineData(true, "CancelButton")]
    [InlineData(true, "Escape")]
    [InlineData(true, "Close")]
    public async Task CancellingTheMoveDialogPreservesSelectionAndHistory(bool timeline, string action)
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context, timeline);
        var selected = SelectTargets(context, original, timeline);
        var menu = OpenMenu(context, original, timeline, 1);
        var operation = ExecuteMove(menu, timeline);
        menu.Close();
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<IntegerInputDialog>());
        try
        {
            SetIntegerText(dialog, "125");
            if (action == "Escape")
            {
                dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            }
            else if (action == "Close")
            {
                dialog.Close();
            }
            else
            {
                UiTestActions.Click(dialog, action);
            }
            await operation;
            Flush(context.Window);

            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
            Assert.False(context.Session.Editor.CanRedo);
            Assert.Null(context.Session.LastError);
            AssertSelection(context, selected, timeline);
            Assert.False(dialog.IsVisible);
            Assert.Empty(context.Window.OwnedWindows.OfType<IntegerInputDialog>());
        }
        finally
        {
            dialog.Close((int?)null);
            await operation;
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RightClickingAnUnselectedItemMovesOnlyThatItem(bool timeline)
    {
        await using var context = new MainWindowTestContext();
        var original = Prepare(context, timeline);
        SelectTargets(context, original, timeline);
        var menu = OpenMenu(context, original, timeline, 2);
        var selected = new[] { timeline ? original.Layers[2].Id : original.Subtitles[2].Id };
        AssertSelection(context, selected, timeline);
        var operation = ExecuteMove(menu, timeline);
        menu.Close();
        var dialog = Assert.Single(context.Window.OwnedWindows.OfType<IntegerInputDialog>());
        try
        {
            SetIntegerText(dialog, "125");
            UiTestActions.Click(dialog, "ConfirmButton");
            await operation;
            Flush(context.Window);

            var moved = context.Session.DocumentSnapshot;
            Assert.Same(original.Subtitles[0], moved.Subtitles[0]);
            Assert.Same(original.Subtitles[1], moved.Subtitles[1]);
            var cue = original.Subtitles[2];
            var offset = new MediaTime(125, 1000);
            Assert.Equal(cue with { Start = cue.Start + offset, End = cue.End + offset }, moved.Subtitles[2]);
            Assert.Same(original.Layers[^1], moved.Layers[^1]);
            AssertSelection(context, selected, timeline);
            Assert.True(context.Session.Editor.Undo());
            Assert.Same(original, context.Session.DocumentSnapshot);
            Assert.False(context.Session.Editor.CanUndo);
        }
        finally
        {
            dialog.Close((int?)null);
            await operation;
        }
    }

    private static ProjectDocument Prepare(MainWindowTestContext context, bool timeline)
    {
        var first = new SubtitleLine { Start = new(1), End = new(2), Text = "First" };
        var second = new SubtitleLine { Start = new(3), End = new(4), Text = "Second" };
        var untouched = new SubtitleLine { Start = new(6), End = new(7), Text = "Untouched" };
        var shapeTrack = new ProjectTrack { Name = "Shapes" };
        var shape = new ProjectLayer
        {
            TrackId = shapeTrack.Id, Kind = LayerKind.SHAPE, Start = new(3, 2), End = new(5, 2),
            Shape = new(ShapeKind.RECTANGLE, 30, 40), AnimationOffset = new(1, 3)
        };
        var document = new ProjectDocument
        {
            Tracks = [ProjectTrack.Default, shapeTrack],
            Subtitles = [first, second, untouched],
            Layers = [SubtitleLayer(first), SubtitleLayer(second), SubtitleLayer(untouched), shape]
        };
        context.Session.Editor.Reset(document);
        context.Window.Layouts.Activate(timeline ? "timeline" : "subtitles");
        if (timeline)
        {
            var control = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
            context.ViewModel.Timeline.IsSnapEnabled = false;
            context.ViewModel.Timeline.IsStepEnabled = false;
            control.PixelsPerSecond = 60;
            control.ViewStart = 0;
        }
        Flush(context.Window);
        return document;
    }

    private static Guid[] SelectTargets(MainWindowTestContext context, ProjectDocument document, bool timeline)
    {
        if (timeline)
        {
            var ids = new[] { document.Layers[0].Id, document.Layers[1].Id, document.Layers[^1].Id };
            ClickClip(context, ids[0]);
            ClickClip(context, ids[1], CommandModifier());
            ClickClip(context, ids[2], CommandModifier());
            AssertSelection(context, ids, true);
            return ids;
        }

        var subtitles = document.Subtitles.Take(2).Select(cue => cue.Id).ToArray();
        ClickRow(context, subtitles[0], MouseButton.Left);
        ClickRow(context, subtitles[1], MouseButton.Left, RawInputModifiers.Shift);
        AssertSelection(context, subtitles, false);
        return subtitles;
    }

    private static ContextMenu OpenMenu(MainWindowTestContext context, ProjectDocument document, bool timeline, int cueIndex)
    {
        if (timeline)
        {
            var control = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
            EnsureClipVisible(context, control, document.Layers[cueIndex].Id);
            var point = control.TranslatePoint(control.GetClipRectangle(document.Layers[cueIndex].Id)!.Value.Center,
                context.Window)!.Value;
            Assert.Same(control, context.Window.InputHitTest(point));
            context.Window.MouseDown(point, MouseButton.Right);
            context.Window.MouseUp(point, MouseButton.Right);
            Flush(context.Window);
            return Assert.Single(control.GetVisualAncestors().OfType<TimelinePanelView>()).ClipMenu;
        }

        ClickRow(context, document.Subtitles[cueIndex].Id, MouseButton.Right);
        return UiTestActions.Find<ListBox>(context.Window, "SubtitleList").ContextMenu!;
    }

    private static Task ExecuteMove(ContextMenu menu, bool timeline)
    {
        var name = timeline ? "MoveTimelineClipsMenuItem" : "MoveSubtitleRowsMenuItem";
        var item = menu.Items.OfType<MenuItem>().Single(value => value.Name == name);
        Assert.True(item.Command!.CanExecute(null));
        var command = Assert.IsAssignableFrom<IAsyncRelayCommand>(item.Command);
        return command.ExecuteAsync(null);
    }

    private static void SetIntegerText(IntegerInputDialog dialog, string text)
    {
        var input = UiTestActions.Find<NumericDraftInput>(dialog, "IntegerInput");
        var box = Assert.Single(input.GetVisualDescendants().OfType<TextBox>());
        Assert.True(box.Focus());
        box.SelectAll();
        dialog.KeyTextInput(text);
        Flush(dialog);
        Assert.Equal(text, input.RawText);
    }

    private static void ClickClip(MainWindowTestContext context, Guid layerId,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var control = UiTestActions.Find<SubtitleTimelineControl>(context.Window, "Timeline");
        EnsureClipVisible(context, control, layerId);
        var point = control.TranslatePoint(control.GetClipRectangle(layerId)!.Value.Center, context.Window)!.Value;
        Assert.Same(control, context.Window.InputHitTest(point));
        context.Window.MouseDown(point, MouseButton.Left, modifiers);
        context.Window.MouseUp(point, MouseButton.Left, modifiers);
        Flush(context.Window);
    }

    private static void EnsureClipVisible(MainWindowTestContext context, SubtitleTimelineControl control, Guid layerId)
    {
        Flush(context.Window);
        var rectangle = control.GetClipRectangle(layerId)!.Value;
        if (rectangle.Center.Y <= control.RulerHeight || rectangle.Center.Y >= control.Bounds.Height)
        {
            control.SetViewport(control.Viewport with
            {
                VerticalOffset = Math.Max(0, control.Viewport.VerticalOffset + rectangle.Center.Y - control.RulerHeight - 40)
            }, context.ViewModel.Timeline.FullDuration);
            Flush(context.Window);
        }

        var visible = control.GetClipRectangle(layerId)!.Value;
        Assert.InRange(visible.Center.Y, control.RulerHeight + 1, control.Bounds.Height - 1);
    }

    private static void ClickRow(MainWindowTestContext context, Guid cueId, MouseButton button,
        RawInputModifiers modifiers = RawInputModifiers.None)
    {
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        var row = list.Items.OfType<SubtitleRow>().Single(value => value.Id == cueId);
        list.ScrollIntoView(row);
        Flush(context.Window);
        var header = list.GetVisualDescendants().OfType<TextBlock>().Single(control =>
            control.DataContext is SubtitleRow value && value.Id == cueId && Grid.GetColumn(control) == 0 &&
            control.Text == row.ContentType && control.GetVisualParent() is Grid { ColumnDefinitions.Count: 5 });
        var point = header.TranslatePoint(new(header.Bounds.Width / 2, header.Bounds.Height / 2), context.Window)!.Value;
        context.Window.MouseDown(point, button, modifiers);
        context.Window.MouseUp(point, button, modifiers);
        Flush(context.Window);
    }

    private static void AssertSelection(MainWindowTestContext context, Guid[] expected, bool timeline)
    {
        if (timeline)
        {
            Assert.Equal(expected.Order(), context.ViewModel.Timeline.SelectedLayerIds.Order());
            return;
        }

        Assert.Equal(expected.Order(), context.Session.SelectedSubtitleIds.Order());
        var list = UiTestActions.Find<ListBox>(context.Window, "SubtitleList");
        Assert.Equal(expected.Order(), list.Selection.SelectedItems.OfType<SubtitleRow>().Select(row => row.Id).Order());
    }

    private static ProjectLayer SubtitleLayer(SubtitleLine cue)
    {
        return new()
        {
            Kind = LayerKind.SUBTITLE, SubtitleId = cue.Id, Start = cue.Start, End = cue.End,
            Tracks = [new(AnimationProperty.OPACITY, [new(new(1, 2), 0.4)])]
        };
    }

    private static RawInputModifiers CommandModifier()
    {
        return OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.Control;
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
}
