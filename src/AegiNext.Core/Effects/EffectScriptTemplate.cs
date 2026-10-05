namespace AegiNext.Core.Effects;

/// <summary>同时保留可编辑源文本与经过验证的脚本数据。</summary>
public sealed record EffectScriptTemplate(string Source, EffectScript Script);
