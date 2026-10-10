using AegiNext.Core.Projects;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

namespace AegiNext.Desktop.Ui.Tests;

public sealed class EffectsSelectionUiTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ChangingScopeOrStateKeepsUnchangedDropdownItems(bool changeState)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        var id = await UiTestActions.CreateSubtitleAsync(context, text: "ABCDE");
        var range = new SubtitleAnimationRange(Guid.NewGuid(), 1, 2);
        context.Session.Editor.SetSubtitleAnimationRange(id, range);
        var effects = context.ViewModel.Effects;
        var scope = UiTestActions.Find<ComboBox>(context.Window, "EffectScopeCombo");
        var state = UiTestActions.Find<ComboBox>(context.Window, "EffectStateCombo");
        var scopes = effects.Scopes;
        var states = effects.States;
        var scopeItems = scope.ItemsSource;
        var stateItems = state.ItemsSource;
        var scopeResets = 0;
        var stateResets = 0;
        scope.PropertyChanged += (_, change) =>
        {
            if (change.Property == ItemsControl.ItemsSourceProperty)
            {
                scopeResets++;
            }
        };
        state.PropertyChanged += (_, change) =>
        {
            if (change.Property == ItemsControl.ItemsSourceProperty)
            {
                stateResets++;
            }
        };

        if (changeState)
        {
            state.SelectedItem = states.Single(choice => choice.State == SubtitleAnimationState.ACTIVE);
        }
        else
        {
            scope.SelectedItem = scopes.Single(choice => choice.Id == range.Id);
        }
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(changeState ? SubtitleAnimationState.ACTIVE : SubtitleAnimationState.NORMAL, effects.Target.State);
        Assert.Equal(changeState ? null : (Guid?)range.Id, effects.Target.TextRangeId);
        Assert.Same(scopes, effects.Scopes);
        Assert.Same(states, effects.States);
        Assert.Same(scopeItems, scope.ItemsSource);
        Assert.Same(stateItems, state.ItemsSource);
        Assert.Equal(0, scopeResets);
        Assert.Equal(0, stateResets);
        Assert.Same(effects.SelectedScope, scope.SelectedItem);
        Assert.Same(effects.SelectedState, state.SelectedItem);
    }

    [AvaloniaTheory]
    [InlineData("EffectScopeCombo")]
    [InlineData("EffectStateCombo")]
    public async Task InspectorRefreshKeepsAnOpenDropdownAndItsSelection(string name)
    {
        await using var context = new MainWindowTestContext();
        await context.OpenMediaAsync();
        await UiTestActions.CreateSubtitleAsync(context);
        var selector = UiTestActions.Find<ComboBox>(context.Window, name);
        var items = selector.ItemsSource;
        var selection = selector.SelectedItem;
        var changes = 0;
        selector.SelectionChanged += (_, _) => changes++;
        selector.IsDropDownOpen = true;
        context.Window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();

        for (var refresh = 0; refresh < 5; refresh++)
        {
            context.Session.RefreshEffectsInspectorTarget();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(items, selector.ItemsSource);
            Assert.Same(selection, selector.SelectedItem);
            Assert.True(selector.IsDropDownOpen);
        }
        Assert.Equal(0, changes);
        selector.IsDropDownOpen = false;
    }
}
