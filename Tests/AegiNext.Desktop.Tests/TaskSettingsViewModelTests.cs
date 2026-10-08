using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Tasks;
using System.Text.Json;

namespace AegiNext.Desktop.Tests;

public sealed class TaskSettingsViewModelTests
{
    [Fact]
    public void OldPreferencesUseDefaultAndNewValueParticipatesInEqualityAndSerialization()
    {
        var original = JsonSerializer.Deserialize<WorkbenchPreferences>("{\"Version\":1}")!;
        original.Validate();
        Assert.Equal(4, original.MaximumConcurrentTasks);
        var changed = original with { MaximumConcurrentTasks = 32 };
        Assert.NotEqual(original, changed);
        Assert.Equal(changed, JsonSerializer.Deserialize<WorkbenchPreferences>(JsonSerializer.Serialize(changed)));
        Assert.Equal(changed.GetHashCode(), (original with { MaximumConcurrentTasks = 32 }).GetHashCode());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void InvalidPersistedLimitIsRejected(int maximum)
    {
        Assert.Throws<InvalidDataException>(() => new WorkbenchPreferences { MaximumConcurrentTasks = maximum }.Validate());
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("32", 32)]
    public void ConfirmedLimitEmitsOnceAndPreferenceRefreshDoesNotEmit(string text, int maximum)
    {
        var model = new TaskSettingsViewModel(new());
        var changes = new List<int>();
        model.Changed += (_, args) => changes.Add(args.MaximumConcurrentTasks);
        model.MaximumConcurrentTasksText = text;
        Assert.True(model.CommitMaximumConcurrentTasks());
        Assert.True(model.CommitMaximumConcurrentTasks());
        model.UpdatePreferences(new() { MaximumConcurrentTasks = maximum, Theme = WorkbenchTheme.DARK });
        Assert.Equal(maximum, Assert.Single(changes));
        Assert.Null(model.Error);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("33")]
    [InlineData("1.5")]
    [InlineData("7e-")]
    [InlineData("")]
    public void InvalidLimitSurvivesPreferenceAndLanguageRefreshAndEscapeRestoresLatestValue(string text)
    {
        var model = new TaskSettingsViewModel(new());
        var changes = 0;
        model.Changed += (_, _) => changes++;
        model.MaximumConcurrentTasksText = text;
        Assert.False(model.CommitMaximumConcurrentTasks());
        model.UpdatePreferences(new() { MaximumConcurrentTasks = 8 });
        model.RefreshLanguage();
        Assert.Equal(text, model.MaximumConcurrentTasksText);
        Assert.NotNull(model.Error);
        Assert.Equal(0, changes);
        model.RestoreMaximumConcurrentTasks();
        Assert.Equal("8", model.MaximumConcurrentTasksText);
        Assert.Null(model.Error);
    }
}
