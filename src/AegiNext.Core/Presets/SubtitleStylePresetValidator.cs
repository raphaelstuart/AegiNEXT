using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using AegiNext.Core.Projects;

namespace AegiNext.Core.Presets;

/// <summary>验证便携样式、字体内容完整性及整包预算，不访问文件系统。</summary>
public static class SubtitleStylePresetValidator
{
    [SuppressMessage("Naming", "CA1707:Identifiers should not contain underscores", Justification = "项目常量采用 ALL_UPPER。")]
    public const int MAXIMUM_FONT_BYTES = 32 * 1024 * 1024;
    private const long MAXIMUM_TOTAL_FONT_BYTES = 64L * 1024 * 1024;
    private static readonly HashSet<string> fontExtensions = new(StringComparer.OrdinalIgnoreCase) { ".ttf", ".otf", ".ttc", ".otc" };

    /// <summary>验证一个不携带工程字体标识的自包含样式。</summary>
    public static void Validate(SubtitleStylePreset preset)
    {
        Require(preset is not null && preset.Id != Guid.Empty, "预设标识无效。");
        ValidateName(preset.Name);
        ProjectValidator.ValidateSubtitleStyle(preset.Style);
        Require(preset.Style.FontAssetId is null, "便携样式不能持有项目字体标识；请捕获并内嵌字体。");
        if (preset.Font is { } font)
        {
            ValidateFont(font);
        }

        try
        {
            preset.TimingPostProcessor?.Validate();
        }
        catch (ArgumentOutOfRangeException error)
        {
            throw new InvalidDataException("字幕样式关联的时间后续处理器参数无效。", error);
        }
    }

    /// <summary>验证版本、重复标识和名称，以及整个集合的字体总预算。</summary>
    public static void Validate(SubtitleStylePresetCollection collection)
    {
        Require(collection is not null && collection.Version == SubtitleStylePresetCollection.CURRENT_VERSION,
            "不支持的字幕样式库版本。");
        Require(!collection.Presets.IsDefault && collection.Presets.Length <= 256, "样式库集合无效或超过 256 项。");
        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        foreach (var preset in collection.Presets)
        {
            Require(preset is not null, "预设不能为 null。");
            total += preset.Font is { } font && !font.Data.IsDefault ? font.Data.Length : 0;
            Require(total <= MAXIMUM_TOTAL_FONT_BYTES, "样式库内嵌字体总量超过 64 MiB。");
            Validate(preset);
            Require(ids.Add(preset.Id) && names.Add(preset.Name), "预设标识或名称重复；名称不区分大小写。");
        }
    }

    private static void ValidateName(string name)
    {
        Require(name is { Length: > 0 and <= 128 }, "预设名称长度必须为 1 至 128。");
        ProjectValidator.ValidateText(name);
        Require(!name.Any(char.IsControl) && name == name.Trim() && name.IsNormalized(NormalizationForm.FormC),
            "预设名称必须采用 NFC Unicode，且不能包含控制字符或首尾空白。");
    }

    private static void ValidateFont(EmbeddedSubtitleFont font)
    {
        var name = font.FileName;
        Require(name is { Length: > 0 and <= 128 }, "内嵌字体文件名无效。");
        ProjectValidator.ValidateText(name);
        Require(!name.Any(character => char.IsControl(character) || "/\\:<>\"|?*".Contains(character, StringComparison.Ordinal)) &&
            !name.EndsWith('.') && !name.EndsWith(' ') && name.IsNormalized(NormalizationForm.FormC) &&
            fontExtensions.Contains(Path.GetExtension(name)), "内嵌字体必须使用规范的 TTF、OTF、TTC 或 OTC 文件名。");
        var stem = name.Split('.')[0];
        Require(!IsWindowsDeviceName(stem), "内嵌字体不能使用平台保留文件名。");
        Require(!font.Data.IsDefaultOrEmpty && font.Data.Length <= MAXIMUM_FONT_BYTES, "内嵌字体为空或超过 32 MiB。");
        Require(font.Sha256 is { Length: 64 } && font.Sha256.All(character => char.IsAsciiDigit(character) || character is >= 'a' and <= 'f'),
            "字体 SHA-256 必须是 64 位小写十六进制。");
        var actual = Convert.ToHexStringLower(SHA256.HashData(font.Data.AsSpan()));
        Require(string.Equals(actual, font.Sha256, StringComparison.Ordinal), "内嵌字体内容与 SHA-256 不一致。");
    }

    private static bool IsWindowsDeviceName(string stem)
    {
        return stem.Equals("CON", StringComparison.OrdinalIgnoreCase) || stem.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            stem.Equals("AUX", StringComparison.OrdinalIgnoreCase) || stem.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            stem.Length == 4 && stem[3] is >= '1' and <= '9' &&
            (stem.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("LPT", StringComparison.OrdinalIgnoreCase));
    }

    private static void Require([DoesNotReturnIf(false)] bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidDataException(message);
        }
    }
}
