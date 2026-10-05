namespace AegiNext.Core.Effects;

/// <summary>片段不足以容纳固定段时，按比例压缩固定段或拒绝应用。</summary>
public enum EffectScriptShortClipPolicy
{
    COMPRESS,
    REJECT
}
