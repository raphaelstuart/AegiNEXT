namespace AegiNext.Desktop.Settings.Projects;

/// <summary>携带已验证的项目设置，不包含未确认草稿。</summary>
public sealed class ProjectPreferencesChangedEventArgs(ProjectPreferences preferences) : EventArgs
{
    public ProjectPreferences Preferences { get; } = preferences;
}
