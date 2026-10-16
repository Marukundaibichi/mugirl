using System.Linq;
using System.Xml;

namespace Mugirl.SaveUpgrade;

/// <summary>
/// Def 表中的删除行：按结构删除依赖对象（物品、账单、健康状态），并处理原版过滤器与登记的 Def 值上下文中的引用。
/// 被删对象的加载 ID 在应用时自动收集，被删配方单独记录，最后的加载 ID 步骤置空所有指向它们的引用。
/// </summary>
/// <remarks>
/// 存档中的 <c>&lt;def&gt;</c> 文本没有类型（同名 Def 可能同时是 ThingDef 和 HediffDef），所以只在类型确定的位置处理：
/// 物品元素（ThingDef）、<c>hediffSet/hediffs</c>（HediffDef）、账单的 <c>recipe</c>（RecipeDef）、ThingFilter 的两个集合，
/// 以及模块登记的 Def 值上下文。其他位置由 Scribe_Defs 读取的残留引用读为 null，<c>WasDefRemoved</c> 后缀让原版不报错。
/// </remarks>
internal sealed class DefDropStep : DomUpgradeStep
{
    internal const string StepId = "Framework.Defs";

    /// <summary>原版保存 Def 引用的集合：任何 ThingFilter 的 <c>allowedDefs</c>（ThingDef）与 <c>disallowedSpecialFilters</c>。</summary>
    internal static readonly IReadOnlyList<ValueContext> VanillaContexts =
    [
        new("Vanilla.ThingFilter.AllowedDefs", OwnerSelector.Any, "allowedDefs", ValueContextKind.DefReference, "ThingDef", ValuePart.Items, UpgradeTableBuilder.FrameworkModule),
        new("Vanilla.ThingFilter.DisallowedSpecialFilters", OwnerSelector.Any, "disallowedSpecialFilters", ValueContextKind.DefReference, "SpecialThingFilterDef", ValuePart.Items, UpgradeTableBuilder.FrameworkModule),
    ];

    internal DefDropStep()
        : base(StepId, "Remove things, bills and hediffs of dropped defs and the references to them in typed contexts.")
    {
    }

    internal override IEnumerable<string> IndexNames => VanillaContexts.Select(c => c.Label);

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        UpgradeTables tables = context.Tables;
        List<DefRow> drops = [.. tables.DefRows.Concat(tables.DerivedDefRows).Where(row => row.New is null)];
        if (drops.Count == 0)
        {
            return;
        }

        SaveIndex index = context.Index;
        foreach (DefRow row in drops)
        {
            switch (row.Policy)
            {
                case DefDropPolicy.RemoveThings:
                    foreach (IndexedThing thing in index.ThingsWithDef(row.Old))
                    {
                        if (context.IsLive(thing.Element))
                        {
                            plan.RemoveItem(thing.Element, NullForm.Value);
                        }
                    }

                    break;
                case DefDropPolicy.RemoveBills:
                    plan.RemovedRecipes.Add(row.Old);
                    foreach (XmlElement bill in index.Bills)
                    {
                        if (bill["recipe"]?.InnerText == row.Old && context.IsLive(bill))
                        {
                            plan.RemoveItem(bill, NullForm.Value);
                        }
                    }

                    break;
                case DefDropPolicy.RemoveHediffs:
                    foreach (IndexedHediff hediff in index.Hediffs)
                    {
                        if (hediff.Def == row.Old && context.IsLive(hediff.Element))
                        {
                            plan.RemoveItem(hediff.Element, NullForm.Value);
                        }
                    }

                    break;
                default:
                    break;
            }
        }

        ILookup<string, string> dropsByRoot = drops.ToLookup(row => row.Root, row => row.Old, StringComparer.Ordinal);
        foreach (ValueContext valueContext in VanillaContexts.Concat(tables.ValueContexts))
        {
            if (!valueContext.HoldsDefNames || !dropsByRoot.Contains(valueContext.DefRoot!))
            {
                continue;
            }

            var dropped = new HashSet<string>(dropsByRoot[valueContext.DefRoot!], StringComparer.Ordinal);
            foreach (XmlElement label in index.ElementsNamed(valueContext.Label))
            {
                if (!context.IsLive(label) || label.ParentNode is not XmlElement owner || !valueContext.Owner.Matches(owner, context))
                {
                    continue;
                }
                if (VanillaContexts.Contains(valueContext) && !TypedDefReferenceScope.IsThingFilterList(label, context))
                {
                    continue;
                }

                foreach (XmlElement leaf in ValueLeaves.Of(label, valueContext.Part))
                {
                    if (dropped.Contains(leaf.InnerText))
                    {
                        ValueLeaves.Remove(plan, label, leaf, valueContext.NullForm);
                    }
                }
            }
        }
    }
}
