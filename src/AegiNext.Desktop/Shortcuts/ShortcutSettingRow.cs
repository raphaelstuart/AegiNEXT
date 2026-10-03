using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AegiNext.Desktop.Shortcuts;

/// <summary>设置列表的一行，输入草稿与已提交绑定分离。</summary>
public sealed class ShortcutSettingRow : INotifyPropertyChanged
{
    private string gesture;

    /// <summary>以稳定命令、已本地化名称和原手势创建可编辑行。</summary>
    public ShortcutSettingRow(WorkbenchCommand command, string displayName, string gesture)
    {
        if (!Enum.IsDefined(command))
        {
            throw new ArgumentOutOfRangeException(nameof(command));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentNullException.ThrowIfNull(gesture);
        Command = command;
        DisplayName = displayName;
        this.gesture = gesture;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public WorkbenchCommand Command { get; }
    public string DisplayName { get; }
    public string Gesture
    {
        get => gesture;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (gesture != value)
            {
                gesture = value;
                OnPropertyChanged();
            }
        }
    }

    /// <summary>校验并生成独立绑定；配置级唯一性由 ShortcutConfiguration.Validate 验证。</summary>
    public ShortcutBinding ToBinding(bool? isMacOs = null)
    {
        return new(Command, ShortcutConfiguration.NormalizeGesture(Gesture, isMacOs));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
    {
        PropertyChanged?.Invoke(this, new(name));
    }
}
