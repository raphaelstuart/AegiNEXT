using System.Collections.Immutable;

namespace AegiNext.Core.Effects;

/// <summary>从随程序嵌入的同一份样板读取内置脚本，避免文档与运行行为分叉。</summary>
public static class BuiltinEffectScripts
{
    private const string RESOURCE_PREFIX = "AegiNext.Effects.";
    private const string RESOURCE_SUFFIX = ".aegifx";
    private static readonly Lazy<ImmutableArray<EffectScriptTemplate>> templates = new(Load);

    public static ImmutableArray<EffectScriptTemplate> Templates => templates.Value;

    /// <summary>按稳定标识返回内置源文本和解析结果；未知标识明确失败。</summary>
    public static EffectScriptTemplate Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return Templates.FirstOrDefault(template => string.Equals(template.Script.Id, id, StringComparison.Ordinal)) ??
            throw new KeyNotFoundException($"内置特效脚本不存在：{id}。");
    }

    private static ImmutableArray<EffectScriptTemplate> Load()
    {
        var assembly = typeof(BuiltinEffectScripts).Assembly;
        var result = ImmutableArray.CreateBuilder<EffectScriptTemplate>();
        foreach (var resource in assembly.GetManifestResourceNames().Where(name => name.StartsWith(RESOURCE_PREFIX, StringComparison.Ordinal) &&
                     name.EndsWith(RESOURCE_SUFFIX, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource) ?? throw new InvalidDataException($"内置脚本资源缺失：{resource}。");
            using var reader = new StreamReader(stream);
            var source = reader.ReadToEnd();
            var script = EffectScriptParser.Parse(source);
            if (resource != RESOURCE_PREFIX + script.Id + RESOURCE_SUFFIX || result.Any(template => template.Script.Id == script.Id))
            {
                throw new InvalidDataException($"内置脚本标识重复或与文件名不一致：{resource}。");
            }

            result.Add(new(source, script));
        }

        if (result.Count == 0)
        {
            throw new InvalidDataException("没有随程序发布的内置特效脚本。");
        }

        return result.ToImmutable();
    }
}
