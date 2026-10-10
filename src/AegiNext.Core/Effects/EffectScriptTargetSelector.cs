namespace AegiNext.Core.Effects;

/// <summary>不携带工程范围身份的模板目标；固定范围采用一基字素起点和字素数量。</summary>
public sealed record EffectScriptTargetSelector(EffectScriptTargetKind Kind, int Start = 1, int Count = 1);
