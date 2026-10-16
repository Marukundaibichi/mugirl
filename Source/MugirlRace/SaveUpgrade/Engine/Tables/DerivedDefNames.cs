namespace Mugirl.SaveUpgrade;

/// <summary>
/// 原版按其他 Def 的 defName 生成的隐含 Def（1.7.4979 反编译）：<c>ThingDefGenerator_Buildings</c>（建筑与地形的 <c>Blueprint_</c>、
/// <c>Frame_</c>，建筑的 <c>Blueprint_Install_</c>）、<c>ThingDefGenerator_Corpses</c>（<c>Corpse_</c>、<c>UnnaturalCorpse_</c>）、<c>ThingDefGenerator_Meat</c>（<c>Meat_</c>）、
/// <c>RecipeDefGenerator</c>（<c>Make_</c>、<c>Make_…Bulk</c>、<c>Administer_</c>）、<c>ThingDefGenerator_Techprints</c>（<c>Techprint_</c>）、
/// <c>ThingDefGenerator_Neurotrainer</c>（技能的 <c>Neurotrainer_</c>、灵能的 <c>Psytrainer_</c>）。
/// 来源 Def 改名或删除时，这些名称跟着改名或删除；显式登记的行优先。
/// </summary>
/// <remarks>
/// 规则按根类型生成，不判断来源 Def 是否真的会生成隐含 Def（例如不是灵能的 AbilityDef 没有 <c>Psytrainer_</c>）：
/// 多出的行只匹配存档中不存在的名称，没有副作用。异种基因、思想等其他隐含 Def 的来源由验证器检查（Mugirl 目前没有）。
/// </remarks>
internal static class DerivedDefNames
{
    internal static readonly IReadOnlyList<DerivedDefRule> Rules =
    [
        new("ThingDef", "ThingDef", "Blueprint_", ""),
        new("ThingDef", "ThingDef", "Blueprint_Install_", ""),
        new("ThingDef", "ThingDef", "Frame_", ""),
        new("ThingDef", "ThingDef", "Corpse_", ""),
        new("ThingDef", "ThingDef", "UnnaturalCorpse_", ""),
        new("ThingDef", "ThingDef", "Meat_", ""),
        new("ThingDef", "RecipeDef", "Make_", ""),
        new("ThingDef", "RecipeDef", "Make_", "Bulk"),
        new("ThingDef", "RecipeDef", "Administer_", ""),
        new("TerrainDef", "ThingDef", "Blueprint_", ""),
        new("TerrainDef", "ThingDef", "Frame_", ""),
        new("ResearchProjectDef", "ThingDef", "Techprint_", ""),
        new("SkillDef", "ThingDef", "Neurotrainer_", ""),
        new("AbilityDef", "ThingDef", "Psytrainer_", ""),
    ];

    /// <summary>由 <paramref name="row"/> 派生的行（与来源行同一结构版本）。</summary>
    internal static IEnumerable<DefRow> Expand(DefRow row)
    {
        ArgumentNullException.ThrowIfNull(row);
        foreach (DerivedDefRule rule in Rules)
        {
            if (rule.SourceRoot != row.Root)
            {
                continue;
            }

            DefDropPolicy? policy = row.New is not null
                ? null
                : rule.TargetRoot == "RecipeDef" ? DefDropPolicy.RemoveBills : DefDropPolicy.RemoveThings;
            yield return new DefRow(
                rule.TargetRoot,
                rule.Prefix + row.Old + rule.Suffix,
                row.New is null ? null : rule.Prefix + row.New + rule.Suffix,
                policy,
                row.Module,
                "Implied " + rule.TargetRoot + " " + rule.Prefix + "*" + rule.Suffix + " of " + row.Root + " " + row.Old + ".",
                row.Schema,
                row.Old);
        }
    }
}
