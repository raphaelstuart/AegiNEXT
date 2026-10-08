using AegiNext.Application.Tasks;
using AegiNext.Desktop.Settings;

namespace AegiNext.Desktop.Startup;

internal sealed class PreferencesWriteTask(DesktopApplicationContext owner, WorkbenchPreferences preferences) : AegiTask
{
    public override string Name => "Tasks.PreferencesSave";
    public override bool CanCancel => false;
    public override string CoalescingKey => owner.PreferencesStore.DirectoryPath;
    public override IReadOnlyCollection<AegiTaskResource> Resources =>
        [AegiTaskResource.DeferredStoragePath(Path.Combine(owner.PreferencesStore.DirectoryPath, "preferences.json"))];

    protected override Task ExecuteAsync(AegiTaskExecutionContext context)
    {
        context.EnterCommit();
        return owner.SavePreferencesAsync(preferences);
    }
}
