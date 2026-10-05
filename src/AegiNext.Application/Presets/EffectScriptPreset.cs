namespace AegiNext.Application.Presets;

/// <summary>个人特效库的稳定身份、展示名称及可编辑脚本源。</summary>
public sealed record EffectScriptPreset(Guid Id, string Name, string Source);
