namespace AegiNext.Core.Effects;

/// <summary>脚本中的一基轮廓及节点序号；编译到目标 Clip 时解析为稳定节点标识。</summary>
public readonly record struct EffectScriptNodeSelector(int ContourNumber, int NodeNumber);
