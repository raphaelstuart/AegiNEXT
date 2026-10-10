namespace AegiNext.Core.Effects;

/// <summary>脚本支持的有限文字分组方式，不执行正则表达式或自然语言分词。</summary>
public enum EffectScriptUnitKind
{
    GROUP,
    GRAPHEME,
    CHUNK,
    WORD,
    LINE,
    PARAGRAPH,
    SPLIT
}
