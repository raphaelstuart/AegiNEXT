using AegiNext.Desktop.I18n;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Settings.Shortcuts;

/// <summary>单一快捷键列表中的分区标题或命令行；只有命令行参与选择和编辑。</summary>
public sealed class ShortcutSettingsListItem
{
    /// <summary>创建不参与选择的本地化分区标题。</summary>
    public ShortcutSettingsListItem(string sectionTitleKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sectionTitleKey);
        SectionTitleKey = sectionTitleKey;
    }

    /// <summary>引用现有命令草稿，不创建第二份绑定状态。</summary>
    public ShortcutSettingsListItem(ShortcutSettingRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        Row = row;
    }

    public ShortcutSettingRow? Row { get; }
    public string? SectionTitleKey { get; }
    public string SectionTitle => SectionTitleKey is null ? string.Empty : Localization.Get(SectionTitleKey);
    public bool IsCommand => Row is not null;
}
