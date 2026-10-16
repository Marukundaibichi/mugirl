namespace Mugirl.SaveUpgrade;

/// <summary>一条原版隐含 Def 命名规则：<c>Prefix + 来源 defName + Suffix</c>。</summary>
internal readonly record struct DerivedDefRule(string SourceRoot, string TargetRoot, string Prefix, string Suffix);
