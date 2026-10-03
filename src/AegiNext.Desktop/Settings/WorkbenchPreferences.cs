using System.Collections.Immutable;
using System.Buffers;
using AegiNext.Desktop.Shortcuts;

namespace AegiNext.Desktop.Settings;

/// <summary>可持久化并立即应用的桌面偏好。</summary>
public sealed record WorkbenchPreferences
{
    private static readonly SearchValues<char> hexadecimalCharacters = SearchValues.Create("0123456789abcdefABCDEF");
    public int Version { get; init; } = 1;
    public string Language { get; init; } = "system";
    public WorkbenchTheme Theme { get; init; }
    public string AccentColor { get; init; } = "#5273E8";
    public ImmutableArray<ShortcutBinding> ShortcutBindings { get; init; } = ShortcutDefaults.CreateBindings();
    public float Volume { get; init; } = 1;
    public bool WindowMenuOnMac { get; init; }

    /// <summary>拒绝未知设置版本、语言、主题或非法音量。</summary>
    public void Validate()
    {
        if (Version != 1 || Language is not ("system" or "zh-CN" or "en-US") ||
            !Enum.IsDefined(Theme) || !float.IsFinite(Volume) || Volume is < 0 or > 1 ||
            AccentColor is null || AccentColor.Length != 7 || AccentColor[0] != '#' ||
            AccentColor.AsSpan(1).ContainsAnyExcept(hexadecimalCharacters) || ShortcutBindings.IsDefault)
        {
            throw new InvalidDataException("桌面偏好无效或版本不受支持。");
        }

        ShortcutConfiguration.Validate(ShortcutBindings);
        if (ShortcutBindings.Length != Enum.GetValues<WorkbenchCommand>().Length)
        {
            throw new InvalidDataException("快捷键设置必须包含所有命令，禁用请使用空手势。");
        }
    }

    /// <summary>按设置值和快捷键内容比较偏好快照。</summary>
    public bool Equals(WorkbenchPreferences? other)
    {
        return other is not null && Version == other.Version && Language == other.Language && Theme == other.Theme &&
               AccentColor == other.AccentColor && Volume.Equals(other.Volume) && WindowMenuOnMac == other.WindowMenuOnMac &&
               ShortcutBindings.AsSpan().SequenceEqual(other.ShortcutBindings.AsSpan());
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Version);
        hash.Add(Language);
        hash.Add(Theme);
        hash.Add(AccentColor);
        hash.Add(Volume);
        hash.Add(WindowMenuOnMac);
        foreach (var binding in ShortcutBindings)
        {
            hash.Add(binding);
        }

        return hash.ToHashCode();
    }
}
