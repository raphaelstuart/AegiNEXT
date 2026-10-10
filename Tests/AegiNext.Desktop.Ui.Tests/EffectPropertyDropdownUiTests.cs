using AegiNext.Core.Projects;
using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Panels.Effects;
using AegiNext.Desktop.Workspace;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectPropertyDropdownUiTests
{
    [AvaloniaTheory]
    [InlineData(320, "en-US", false)]
    [InlineData(320, "zh-CN", true)]
    [InlineData(480, "en-US", true)]
    [InlineData(480, "zh-CN", false)]
    public async Task OpeningFromNumericFocusAndRefreshingKeepsPopupItemsAndGeometryStable(double width, string language, bool dark)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        context.Session.Editor.SetClipMask(context.Session.SelectedLayerId!.Value, new RectangleClipMask { BottomRight = new(400, 300) });
        context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
        using var view = new EffectsPanelView(context.ViewModel.Effects, context.Session);
        var window = new Window { Width = width, Height = 900, Content = view, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Flush(window);
            UiTestActions.ExpandEffectsCategory(window, "CompositeCategory");
            UiTestActions.ExpandEffectsCategory(window, "AnimationCategory");
            var choice = UiTestActions.Find<ComboBox>(view, "PropertyCombo");
            choice.BringIntoView();
            Flush(window);
            var input = UiTestActions.Find<NumericUpDown>(view, "OpacityInput");
            input.BringIntoView();
            Flush(window);
            Assert.True(input.GetVisualDescendants().OfType<TextBox>().Single().Focus());
            choice.BringIntoView();
            Flush(window);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var frame = window.CaptureRenderedFrame();
            var items = choice.ItemsSource;
            var selection = choice.SelectedItem;
            var point = choice.TranslatePoint(new Point(choice.Bounds.Width / 2, choice.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Flush(window);
            Assert.True(choice.IsDropDownOpen);
            Assert.Same(items, choice.ItemsSource);
            Assert.Same(selection, choice.SelectedItem);
            var popup = UiTestActions.Find<Popup>(choice, "PART_Popup");
            var presenter = Assert.IsAssignableFrom<Control>(popup.Child);
            var container = choice.ContainerFromIndex(choice.SelectedIndex);
            Assert.NotNull(container);
            var bounds = presenter.Bounds;
            var detached = 0;
            presenter.DetachedFromVisualTree += (_, _) => detached++;
            var sourceChanges = 0;
            choice.PropertyChanged += (_, e) => sourceChanges += e.Property == ItemsControl.ItemsSourceProperty ? 1 : 0;
            for (var index = 0; index < 10; index++)
            {
                context.Session.MaskEditing.Refresh();
                await context.Controller.SeekAsync(new(index, 10));
                Flush(window);
                Assert.True(choice.IsDropDownOpen);
                Assert.Same(items, choice.ItemsSource);
                Assert.Same(selection, choice.SelectedItem);
                Assert.Same(container, choice.ContainerFromIndex(choice.SelectedIndex));
                Assert.Equal(bounds, presenter.Bounds);
            }
            Assert.Equal(0, sourceChanges);
            Assert.Equal(0, detached);
            Assert.True(choice.Focus());
            window.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            window.KeyRelease(Key.Escape, RawInputModifiers.None, PhysicalKey.None, null);
            Flush(window);
            Assert.False(choice.IsDropDownOpen);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public async Task RealChoiceChangesStillRefreshTitlesMaskShapeAndStableNodeTargets()
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var id = context.Session.SelectedLayerId!.Value;
        var choice = UiTestActions.Find<ComboBox>(context.Window, "PropertyCombo");
        var plain = choice.ItemsSource;
        context.Session.Editor.SetClipMask(id, new RectangleClipMask { BottomRight = new(400, 300) });
        Dispatcher.UIThread.RunJobs();
        Assert.NotSame(plain, choice.ItemsSource);
        Assert.Contains(choice.Items.OfType<AnimationPropertyChoice>(), item => item.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        var first = new MaskNode { Position = new(10, 10) };
        var second = new MaskNode { Position = new(100, 100) };
        context.Session.Editor.SetClipMask(id, new VectorClipMask { Contours = [new() { Nodes = [first, second] }] });
        context.Session.MaskEditing.SelectNode(first.Id);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(choice.Items.OfType<AnimationPropertyChoice>(), item => item.Property == AnimationProperty.MASK_RECTANGLE_TOP_LEFT);
        var target = new AnimationTrackTarget(AnimationProperty.MASK_NODE_POSITION, first.Id);
        choice.SelectedItem = choice.Items.OfType<AnimationPropertyChoice>().Single(item => item.Property == target.Property);
        Dispatcher.UIThread.RunJobs();
        var firstItems = choice.ItemsSource;
        context.Session.MaskEditing.Refresh();
        Assert.Same(firstItems, choice.ItemsSource);
        context.Session.MaskEditing.SelectNode(second.Id);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(firstItems, choice.ItemsSource);
        Assert.Equal(AnimationProperty.MASK_NODE_POSITION, Assert.IsType<AnimationPropertyChoice>(choice.SelectedItem).Property);
        Assert.Equal(new(AnimationProperty.MASK_NODE_POSITION, second.Id), context.ViewModel.Effects.Target);
        foreach (var language in new[] { "en-US", "zh-CN" })
        {
            context.Session.UpdatePreferences(context.Session.Preferences with { Language = language });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(Localization.Get("Workbench.MASK_NODE_POSITION"), Assert.IsType<AnimationPropertyChoice>(choice.SelectedItem).Title);
            var localized = choice.ItemsSource;
            context.Session.MaskEditing.Refresh();
            Assert.Same(localized, choice.ItemsSource);
        }
        context.Session.MaskEditing.Clear();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(choice.Items.OfType<AnimationPropertyChoice>(), item => AnimationPropertyMetadata.IsMaskProperty(item.Property));
        Assert.Equal(AnimationProperty.OPACITY, Assert.IsType<AnimationPropertyChoice>(choice.SelectedItem).Property);
        Assert.True(context.Session.Editor.Undo());
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(choice.Items.OfType<AnimationPropertyChoice>(), item => item.Property == AnimationProperty.MASK_POSITION);
    }

    private static void Flush(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
}
