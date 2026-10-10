using System.Collections.Immutable;

namespace AegiNext.Core.Effects;

/// <summary>作用范围的声明式分组定义；固定分块按完整字素计数，分隔符为解码后的文字常量。</summary>
public sealed record EffectScriptUnit(EffectScriptUnitKind Kind)
{
    public int Count { get; init; } = 1;
    public ImmutableArray<string> Delimiters { get; init; } = [];
}
