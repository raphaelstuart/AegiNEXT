namespace AegiNext.Core.Projects;

/// <summary>脚本生成文字范围的稳定来源；保留模板、作用域、父范围和分组定义以便精确重新应用。</summary>
public sealed record SubtitleAnimationRangeOrigin(string EffectId, string ScopeName, Guid? ParentRangeId, string UnitDefinition);
