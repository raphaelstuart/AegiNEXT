using System.Collections.Immutable;
using System.Text.Json;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Shortcuts;
using AegiNext.Desktop.Shortcuts;
using Avalonia.Input;

namespace AegiNext.Desktop.Tests.Shortcuts;

public sealed class SubtitleAuditionShortcutPreferencesTests
{
    [Theory]
    [InlineData(WorkbenchCommand.AUDITION_BEFORE_SUBTITLE, Key.Q)]
    [InlineData(WorkbenchCommand.AUDITION_AFTER_SUBTITLE, Key.W)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE_BEGIN, Key.E)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE, Key.R)]
    public void AuditionDefaultsAreExactBareKeysAndProtectTextInput(WorkbenchCommand command, Key key)
    {
        var router = new ShortcutRouter(ShortcutDefaults.CreateBindings());

        Assert.True(router.TryResolve(key, KeyModifiers.None, false, out var resolved));
        Assert.Equal(command, resolved);
        Assert.False(router.TryResolve(key, KeyModifiers.Shift, false, out _));
        Assert.False(router.TryResolve(key, KeyModifiers.None, true, out _));
    }

    [Theory]
    [InlineData(WorkbenchCommand.ADVANCE_SUBTITLE_ROW, KeyModifiers.None)]
    [InlineData(WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK, KeyModifiers.Shift)]
    public void SubtitleListDefaultsReachTheFocusRouterInText(WorkbenchCommand command, KeyModifiers modifiers)
    {
        var router = new ShortcutRouter(ShortcutDefaults.CreateBindings());

        Assert.True(router.TryResolve(Key.Enter, modifiers, true, out var resolved));
        Assert.Equal(command, resolved);
    }

    [Theory]
    [InlineData(WorkbenchCommand.AUDITION_BEFORE_SUBTITLE, "Q")]
    [InlineData(WorkbenchCommand.AUDITION_AFTER_SUBTITLE, "W")]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE_BEGIN, "E")]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE, "R")]
    [InlineData(WorkbenchCommand.ADVANCE_SUBTITLE_ROW, "Enter")]
    [InlineData(WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK, "Shift+Enter")]
    public async Task PreviousCompleteSettingsKeepCustomBindingsAndDisableOnlyOccupiedAdditions(WorkbenchCommand occupiedCommand, string occupiedGesture)
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var previous = ShortcutDefaults.CreateBindings().Where(binding => binding.Command <= WorkbenchCommand.PASTE_CLIPS)
            .Select(binding => binding.Command switch
            {
                WorkbenchCommand.PLAY_PAUSE => binding with { Gesture = occupiedGesture },
                WorkbenchCommand.SAVE_PROJECT => binding with { Gesture = string.Empty },
                _ => binding
            }).ToImmutableArray();
        var original = new WorkbenchPreferences { ShortcutBindings = previous, Language = "zh-CN", Volume = 0.25f };
        var originalJson = JsonSerializer.Serialize(original);
        await File.WriteAllTextAsync(Path.Combine(directory.Path, "preferences.json"), originalJson);
        using var store = new WorkbenchPreferencesStore(directory.Path);

        var upgraded = store.Load();

        Assert.Null(store.LoadError);
        Assert.Equal(previous.AsEnumerable(), upgraded.ShortcutBindings.Take(previous.Length));
        Assert.Equal(7, upgraded.ShortcutBindings.Length - previous.Length);
        Assert.Equal(original.Language, upgraded.Language);
        Assert.Equal(original.Volume, upgraded.Volume);
        foreach (var addition in upgraded.ShortcutBindings.Skip(previous.Length))
        {
            Assert.Equal(addition.Command == occupiedCommand ? string.Empty :
                ShortcutDefaults.CreateBindings().Single(binding => binding.Command == addition.Command).Gesture, addition.Gesture);
        }
        Assert.Same(upgraded, WorkbenchPreferencesMigration.Upgrade(upgraded));
        Assert.Equal(originalJson, await File.ReadAllTextAsync(Path.Combine(directory.Path, "preferences.json")));
    }

    [Theory]
    [InlineData(WorkbenchCommand.AUDITION_BEFORE_SUBTITLE)]
    [InlineData(WorkbenchCommand.AUDITION_AFTER_SUBTITLE)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE_BEGIN)]
    [InlineData(WorkbenchCommand.AUDITION_SUBTITLE)]
    [InlineData(WorkbenchCommand.ADVANCE_SUBTITLE_ROW)]
    [InlineData(WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK)]
    public async Task ShortcutSettingsExposeRebindAndDisableWithoutRestoringDefaultsAfterRestart(WorkbenchCommand command)
    {
        var model = new ShortcutSettingsViewModel(ShortcutDefaults.CreateBindings());
        model.SelectedRow = Assert.Single(model.Rows, row => row.Command == command);
        ImmutableArray<ShortcutBinding> saved = default;
        model.Changed += (_, args) => saved = args.Bindings;
        model.CaptureGesture("Shift+F6");
        Assert.Equal("Shift+F6", Assert.Single(saved, binding => binding.Command == command).Gesture);
        model.ClearCommand.Execute(null);
        Assert.Equal(string.Empty, Assert.Single(saved, binding => binding.Command == command).Gesture);
        using var directory = new TemporaryWorkbenchDirectory();
        using var store = new WorkbenchPreferencesStore(directory.Path);
        await store.SaveAsync(new() { ShortcutBindings = saved });

        var restored = store.Load();

        Assert.Null(store.LoadError);
        Assert.Equal(string.Empty, Assert.Single(restored.ShortcutBindings, binding => binding.Command == command).Gesture);
        Assert.Same(restored, WorkbenchPreferencesMigration.Upgrade(restored));
    }

    [Theory]
    [InlineData(WorkbenchCommand.ADVANCE_SUBTITLE_ROW)]
    [InlineData(WorkbenchCommand.INSERT_SUBTITLE_LINE_BREAK)]
    public void ReboundListCommandStillRoutesLettersAndControlReturnInText(WorkbenchCommand command)
    {
        var router = new ShortcutRouter([new(command, "A")]);
        Assert.True(router.TryResolve(Key.A, KeyModifiers.None, true, out var letter));
        Assert.Equal(command, letter);
        router = new([new(command, "Control+Enter")]);
        Assert.True(router.TryResolve(Key.Enter, KeyModifiers.Control, true, out var controlReturn));
        Assert.Equal(command, controlReturn);
    }
}
