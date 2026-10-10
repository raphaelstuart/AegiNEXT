using System.Collections.Immutable;
using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Masks;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class MaskNodePresentationUiTests
{
    [AvaloniaFact]
    public async Task ThreeGroupedNodePropertiesResolveTheSelectedPointWithoutRebuildingChoices()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var first = new MaskNode { Position = new(10, 20) };
        var second = new MaskNode { Position = new(30, 40) };
        context.Session.Editor.SetClipMask(context.Session.SelectedLayerId!.Value,
            new VectorClipMask { Contours = [new() { Nodes = [first] }, new() { Nodes = [second] }] });
        UiTestActions.ExpandEffectsCategory(context.Window, "AnimationCategory");
        var choice = UiTestActions.Find<ComboBox>(context.Window, "PropertyCombo");
        var nodeProperties = choice.Items.OfType<AnimationPropertyChoice>().Where(item => AnimationPropertyMetadata.IsNodeProperty(item.Property)).ToArray();
        Assert.Equal(new[] { AnimationProperty.MASK_NODE_POSITION, AnimationProperty.MASK_NODE_IN_HANDLE, AnimationProperty.MASK_NODE_OUT_HANDLE },
            nodeProperties.Select(item => item.Property));
        Assert.Null(context.Session.SceneEditing.MaskNodeId);
        var target = new AnimationTrackTarget(AnimationProperty.MASK_NODE_OUT_HANDLE, first.Id);
        var selected = nodeProperties.Single(item => item.Property == target.Property);
        Assert.Equal(Localization.Get("Workbench.MASK_NODE_OUT_HANDLE"), selected.Title);
        var original = context.Session.DocumentSnapshot;
        choice.SelectedItem = selected;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(first.Id, context.Session.SceneEditing.MaskNodeId);
        Assert.Equal(first.Id, context.ViewModel.Masks.SelectedPoint!.Id);
        Assert.Equal(first.Id, context.ViewModel.Timeline.SelectedMaskNodeId);
        Assert.Equal(target, context.ViewModel.Effects.Target);
        Assert.Same(original, context.Session.DocumentSnapshot);
        var source = choice.ItemsSource;
        choice.BringIntoView();
        Flush(context.Window);
        var pointer = choice.TranslatePoint(new Point(choice.Bounds.Width / 2, choice.Bounds.Height / 2), context.Window)!.Value;
        context.Window.MouseDown(pointer, MouseButton.Left);
        context.Window.MouseUp(pointer, MouseButton.Left);
        Flush(context.Window);
        Assert.True(choice.IsDropDownOpen);
        var container = choice.ContainerFromIndex(choice.SelectedIndex);
        Assert.NotNull(container);
        context.Session.MaskEditing.SelectNode(second.Id);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(source, choice.ItemsSource);
        Assert.Same(selected, choice.SelectedItem);
        Assert.True(choice.IsDropDownOpen);
        Assert.Same(container, choice.ContainerFromIndex(choice.SelectedIndex));
        Assert.Equal(new(AnimationProperty.MASK_NODE_OUT_HANDLE, second.Id), context.ViewModel.Effects.Target);
        Assert.Equal(second.Id, context.ViewModel.Timeline.SelectedMaskNodeId);
        Assert.Equal(second.Id, context.ViewModel.Masks.SelectedPoint!.Id);
        if (Environment.GetEnvironmentVariable("AEGINEXT_MASK_NODE_CAPTURE_DIRECTORY") is { } directory)
        {
            var popup = UiTestActions.Find<Popup>(choice, "PART_Popup");
            var child = Assert.IsAssignableFrom<Control>(popup.Child);
            using var bitmap = new RenderTargetBitmap(new((int)child.Bounds.Width, (int)child.Bounds.Height), new(96, 96));
            bitmap.Render(child);
            Directory.CreateDirectory(directory);
            bitmap.Save(Path.Combine(directory, "grouped-node-property-menu.png"), PngBitmapEncoderOptions.Default);
        }
        choice.IsDropDownOpen = false;
        context.Session.MaskEditing.SelectNode(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(source, choice.ItemsSource);
        Assert.Equal(3, choice.Items.OfType<AnimationPropertyChoice>().Count(item => AnimationPropertyMetadata.IsNodeProperty(item.Property)));
        Assert.Same(original, context.Session.DocumentSnapshot);
    }

    [AvaloniaTheory]
    [InlineData("en-US", false)]
    [InlineData("en-US", true)]
    [InlineData("zh-CN", false)]
    [InlineData("zh-CN", true)]
    public async Task AnimatedPointLabelsRefreshWithoutResettingItemsContainersSelectionOrScroll(string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        var nodes = Enumerable.Range(0, 12).Select(index => new MaskNode { Position = new(index * 10, index * 20) }).ToImmutableArray();
        var layerId = context.Session.SelectedLayerId!.Value;
        context.Session.Editor.SetClipMask(layerId, new VectorClipMask { Contours = [new() { Nodes = nodes }] });
        context.Session.Editor.SetKeyframe(layerId, new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, nodes[0].Id), new(new(0), nodes[0].Position));
        context.Session.Editor.SetKeyframe(layerId, new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, nodes[0].Id), new(new(5), new ScenePoint(100, 200)));
        using var view = new MaskPanelView(context.ViewModel.Masks, context.Session);
        var window = new Window { Width = 480, Height = 1100, Content = view, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            var list = UiTestActions.Find<ListBox>(view, "MaskPointList");
            list.BringIntoView();
            Flush(window);
            list.ScrollIntoView(context.ViewModel.Masks.Points[^1]);
            Flush(window);
            var container = Assert.IsType<ListBoxItem>(list.ContainerFromIndex(11));
            var point = container.TranslatePoint(new Point(container.Bounds.Width / 2, container.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Flush(window);
            Assert.Equal(nodes[^1].Id, context.ViewModel.Masks.SelectedPoint!.Id);
            var source = list.ItemsSource;
            var contours = context.ViewModel.Masks.Contours;
            var items = context.ViewModel.Masks.Points;
            var selection = list.SelectedItem;
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            var offset = scroll.Offset;
            Assert.True(offset.Y > 0);
            var label = items[0].Label;
            var original = context.Session.DocumentSnapshot;
            var sourceChanges = 0;
            list.PropertyChanged += (_, e) => sourceChanges += e.Property == ItemsControl.ItemsSourceProperty ? 1 : 0;
            for (var index = 1; index <= 6; index++)
            {
                await context.Controller.SeekAsync(new(index, 2));
                context.Session.MaskEditing.Refresh();
                Flush(window);
                Assert.Same(source, list.ItemsSource);
                Assert.Same(items, context.ViewModel.Masks.Points);
                Assert.Same(contours, context.ViewModel.Masks.Contours);
                Assert.Same(selection, list.SelectedItem);
                Assert.Same(container, list.ContainerFromIndex(11));
                Assert.Equal(offset, scroll.Offset);
            }
            Assert.NotEqual(label, items[0].Label);
            Assert.Equal(0, sourceChanges);
            Assert.Same(original, context.Session.DocumentSnapshot);
            window.Content = null;
            context.Session.MaskEditing.Refresh();
            window.Content = view;
            Flush(window);
            Assert.Same(items, context.ViewModel.Masks.Points);
            Assert.Equal(nodes[^1].Id, context.ViewModel.Masks.SelectedPoint!.Id);
        }
        finally
        {
            window.Close();
        }
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
