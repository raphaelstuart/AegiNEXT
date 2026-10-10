namespace AegiNext.Core.Effects;

/// <summary>脚本分组在原字幕文字中的连续 UTF-16 范围；解析结果始终位于完整字素边界。</summary>
public sealed record EffectScriptTextGroup(int Utf16Start, int Utf16Length);
