namespace Mugirl.SaveUpgrade;

/// <summary>
/// 只改写已索引物品的身份节点。普通叶文本即使与加载 ID 完全相同，也可能是玩家输入，不能据此改名或删除。
/// 真正的引用由读取它们的 Scribe 登记入口转换；动词自身的身份由其 ExposeData 钩子转换。
/// </summary>
internal sealed class LoadIdStep : DomUpgradeStep
{
    internal const string StepId = "Framework.LoadIds";

    internal LoadIdStep()
        : base(StepId, "Normalize thing identity nodes; remap actual Scribe references during this legacy load.")
    {
    }

    internal override void Plan(UpgradeContext context, EditPlan plan)
    {
        foreach (IndexedThing thing in context.Index.Things)
        {
            if (context.Tables.RenamedDef("ThingDef", thing.Def) is { } newDef
                && LoadIdTokens.IsThingIdOf(thing.Def, thing.Id)
                && context.IsLive(thing.Element))
            {
                string newId = LoadIdTokens.RenamedThingId(thing.Id, newDef);
                if (newId != thing.Id && thing.Element["id"] is { } id)
                {
                    plan.SetText(id, newId);
                }
            }
        }
    }

    /// <summary>
    /// 完整 DOM 流水线成功后才发布映射。索引保留原始身份，删除集合包含所有模块已提交的删除；不再遍历存档。
    /// </summary>
    internal static LoadIdRemapper CreateRemapper(UpgradeContext context)
    {
        var things = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (IndexedThing thing in context.Index.Things)
        {
            if (context.Tables.RenamedDef("ThingDef", thing.Def) is not { } newDef || !context.IsLive(thing.Element))
            {
                continue;
            }

            string? original = LoadIdTokens.IsThingIdOf(thing.Def, thing.Id)
                ? thing.Id
                : LoadIdTokens.OriginalThingId(thing.Def, newDef, thing.Id);
            if (original is not null)
            {
                things[original] = LoadIdTokens.RenamedThingId(original, newDef);
            }
        }

        var recipes = new Dictionary<string, string>(StringComparer.Ordinal);
        AddRecipeRenames(context.Tables.DefRows, recipes);
        AddRecipeRenames(context.Tables.DerivedDefRows, recipes);
        return new LoadIdRemapper(things, recipes, context.RemovedLoadIds, context.RemovedRecipes);
    }

    private static void AddRecipeRenames(IEnumerable<DefRow> rows, Dictionary<string, string> recipes)
    {
        foreach (DefRow row in rows)
        {
            if (row.Root == "RecipeDef" && row.New is { } newName)
            {
                recipes[row.Old] = newName;
            }
        }
    }
}
