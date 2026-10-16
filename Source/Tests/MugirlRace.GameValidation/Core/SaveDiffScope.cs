namespace Mugirl.GameValidation.Core;

/// <summary><see cref="SaveDiff"/> 认定“属于被测 Mod”的范围。</summary>
/// <param name="ModDefNames">被测 Mod 的 defName（任意根类型；比较两份存档时包含两边的名字）。</param>
/// <param name="ModThingDefNames">被测 Mod 的 ThingDef 名（含种族与隐含 Def）。</param>
/// <param name="IsModCompLabel">（ThingDef 名, 标签）是否是被测 Mod 的组件写在物品节点里的标签。</param>
/// <param name="UnresolvedThinkNodeKeys">
/// 兼容旧调用接口；日志中的键不能定位实际 Job，当前比较器不会用它放行差异。
/// </param>
internal sealed record SaveDiffScope(
    IReadOnlySet<string> ModDefNames,
    IReadOnlySet<string> ModThingDefNames,
    Func<string, string, bool> IsModCompLabel,
    IReadOnlySet<string> UnresolvedThinkNodeKeys,
    IReadOnlyDictionary<string, int>? DuplicateColorableComps = null,
    IReadOnlySet<string>? EmptyAbilityApparelDefs = null,
    IReadOnlySet<string>? ExplosiveReferenceListDefs = null,
    IReadOnlySet<string>? DefaultTargetVerbClasses = null,
    IReadOnlySet<string>? VerifiedDuplicateForbiddableThingIds = null);
