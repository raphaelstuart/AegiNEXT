using AegiNext.Core.Effects;

namespace AegiNext.Application.Presets;

/// <summary>验证个人脚本身份、名称、DSL 内容以及库整体约束。</summary>
public static class EffectScriptPresetService
{
    /// <summary>验证单个用户模板；内置脚本的稳定标识不可覆盖。</summary>
    public static EffectScript Validate(EffectScriptPreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        if (preset.Id == Guid.Empty || string.IsNullOrWhiteSpace(preset.Name) || preset.Name.Length > 128 || preset.Source is null)
        {
            throw new InvalidDataException("特效模板需要有效标识和不超过 128 字符的名称。");
        }

        var script = EffectScriptParser.Parse(preset.Source);
        if (BuiltinEffectScripts.Templates.Any(item => item.Script.Id == script.Id))
        {
            throw new InvalidDataException("内置特效脚本不可覆盖；请使用新的脚本标识另存为个人模板。");
        }

        return script;
    }

    /// <summary>检查集合版本、容量、名称和所有稳定标识，任一失败拒绝整份数据。</summary>
    public static void Validate(EffectScriptPresetDocument collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        if (collection.Version != 1 || collection.Presets.IsDefault || collection.Presets.Length > 512)
        {
            throw new InvalidDataException("特效脚本库版本无效或超过 512 个模板。");
        }

        var ids = new HashSet<Guid>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var scriptIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var preset in collection.Presets)
        {
            if (preset is null)
            {
                throw new InvalidDataException("特效模板不能为 null。");
            }

            var script = Validate(preset);
            if (!ids.Add(preset.Id) || !names.Add(preset.Name.Trim()) || !scriptIds.Add(script.Id))
            {
                throw new InvalidDataException("特效模板标识、名称或脚本标识重复。");
            }
        }
    }
}
