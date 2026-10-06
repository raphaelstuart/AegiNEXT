using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Settings.Projects;

namespace AegiNext.Desktop.Tests;

public sealed class ProjectSettingsViewModelTests
{
    [Fact]
    public void InvalidDraftSurvivesUnrelatedUpdatesAndLanguageUntilRestored()
    {
        var model = new ProjectSettingsViewModel(new());
        var changes = new List<ProjectPreferences>();
        model.Changed += (_, change) => changes.Add(change.Preferences);
        model.AutoSaveIntervalText = "2.";

        Assert.False(model.Commit(ProjectSettingsField.AUTO_SAVE_INTERVAL));
        model.BackupEnabled = false;
        model.UpdatePreferences(model.Preferences with { WorkspaceRoot = Path.GetTempPath() });
        model.RefreshLanguage();

        Assert.Equal("2.", model.AutoSaveIntervalText);
        Assert.NotNull(model.Error);
        Assert.False(Assert.Single(changes).BackupEnabled);
        Assert.Equal(2, changes[0].AutoSaveIntervalMinutes);
        model.Restore(ProjectSettingsField.AUTO_SAVE_INTERVAL);
        Assert.Equal("2", model.AutoSaveIntervalText);
        Assert.Null(model.Error);
        Assert.Single(changes);
    }

    [Fact]
    public void ValidDraftCommitsOnceAndOtherUnconfirmedDraftsRemainEditable()
    {
        var model = new ProjectSettingsViewModel(new());
        var changes = new List<ProjectPreferences>();
        model.Changed += (_, change) => changes.Add(change.Preferences);
        model.AutoSaveIntervalText = "3";
        model.MaximumBackupCountText = "bad";

        Assert.True(model.Commit(ProjectSettingsField.AUTO_SAVE_INTERVAL));
        Assert.True(model.Commit(ProjectSettingsField.AUTO_SAVE_INTERVAL));

        Assert.Equal(3, Assert.Single(changes).AutoSaveIntervalMinutes);
        Assert.Equal("bad", model.MaximumBackupCountText);
        Assert.False(model.Commit(ProjectSettingsField.MAXIMUM_BACKUP_COUNT));
        Assert.Single(changes);
    }

    [Fact]
    public void ProjectPageNavigationPreservesDraftAndExposesItsValidationError()
    {
        var model = new SettingsWindowViewModel(new());
        model.PageIndex = (int)SettingsPage.PROJECTS;
        model.Projects.BackupIntervalText = "0";
        Assert.False(model.Projects.Commit(ProjectSettingsField.BACKUP_INTERVAL));

        model.PageIndex = (int)SettingsPage.APPEARANCE;
        model.RefreshLanguage();
        model.PageIndex = (int)SettingsPage.PROJECTS;

        Assert.True(model.IsProjectsVisible);
        Assert.True(model.HasError);
        Assert.Equal("0", model.Projects.BackupIntervalText);
    }
}
