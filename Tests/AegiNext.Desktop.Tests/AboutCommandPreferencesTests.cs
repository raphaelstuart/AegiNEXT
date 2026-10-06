using System.Collections.Immutable;
using System.Text.Json;
using AegiNext.Desktop.Settings;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Tests;

/// <summary>验证新增关于命令时保留已保存的个人偏好和快捷键。</summary>
public sealed class AboutCommandPreferencesTests
{
    /// <summary>完整的旧命令集升级后追加后续默认绑定，保留用户自定义与禁用项。</summary>
    [Fact]
    public async Task PreviousFullCommandSetLoadsWithAboutAndPreservesCustomizedBindings()
    {
        using var directory = new TemporaryWorkbenchDirectory();
        var oldBindings = ShortcutDefaults.CreateBindings()
            .Where(binding => binding.Command <= WorkbenchCommand.CLOSE_PROJECT)
            .Select(binding => binding.Command switch
            {
                WorkbenchCommand.OPEN_PROJECT => binding with { Gesture = "CmdOrCtrl+Alt+O" },
                WorkbenchCommand.SAVE_PROJECT => binding with { Gesture = string.Empty },
                _ => binding
            }).ToImmutableArray();
        var previous = new WorkbenchPreferences
        {
            Language = "zh-CN",
            Theme = WorkbenchTheme.DARK,
            Volume = 0.375f,
            WindowMenuOnMac = true,
            ShortcutBindings = oldBindings
        };
        var path = Path.Combine(directory.Path, "preferences.json");
        var json = JsonSerializer.Serialize(previous);
        await File.WriteAllTextAsync(path, json, CancellationToken.None);
        using var store = new WorkbenchPreferencesStore(directory.Path);

        var upgraded = store.Load();

        Assert.Null(store.LoadError);
        Assert.Equal(previous.Language, upgraded.Language);
        Assert.Equal(previous.Theme, upgraded.Theme);
        Assert.Equal(previous.Volume, upgraded.Volume);
        Assert.Equal(previous.WindowMenuOnMac, upgraded.WindowMenuOnMac);
        Assert.Equal(oldBindings, upgraded.ShortcutBindings.Where(binding => binding.Command <= WorkbenchCommand.CLOSE_PROJECT));
        Assert.Equal(ShortcutDefaults.CreateBindings().Where(binding => binding.Command > WorkbenchCommand.CLOSE_PROJECT),
            upgraded.ShortcutBindings.Where(binding => binding.Command > WorkbenchCommand.CLOSE_PROJECT));
        Assert.Equal(string.Empty, Assert.Single(upgraded.ShortcutBindings, binding => binding.Command == WorkbenchCommand.OPEN_ABOUT).Gesture);
        Assert.Equal(json, await File.ReadAllTextAsync(path, CancellationToken.None));
    }
}
