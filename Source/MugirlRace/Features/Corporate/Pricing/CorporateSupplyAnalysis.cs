using System.Linq;

namespace Mugirl.Features.Corporate;

// 全部物品 Def 的供货来源索引：自然产出、商人货源、可制作、科技等级、受限与可兑现价值。
// 输入只有 Def 与交易设置 Def，因此整个进程只构建一次（Def 重新加载时重建），不随每次读档重扫大型模组列表。
internal sealed class CorporateSupplyAnalysis
{
    internal sealed class Entry
    {
        internal bool natural, sourced, crafted, missingValue, restricted;
        internal TechLevel tech;
        internal decimal redemption;
    }

    private sealed class Conversion
    {
        internal required ThingDef input;
        internal required int count;
        internal required List<ThingDefCountClass> outputs;
    }

    // StaticCacheLifecycle: 进程级，只依赖 Def；Def 重新加载（切换语言）时由 CorporateDefCache 按 ThingDefOf.Silver 的实例重建。
    private static readonly CorporateDefCache<CorporateSupplyAnalysis> SharedCache =
        new(() => new CorporateSupplyAnalysis(CorporateDefOf.Mugirl_CorporateTradeConfig));

    // StaticCacheLifecycle: 进程级只读哨兵，未收录的 Def 一律视为受限且缺少估价；调用方只读取，不修改。
    private static readonly Entry Missing = new Entry { restricted = true, missingValue = true };

    private readonly Dictionary<ThingDef, Entry> entries = new Dictionary<ThingDef, Entry>();

    internal static CorporateSupplyAnalysis Shared => SharedCache.Value;

    internal Entry Get(ThingDef? def) => def != null && entries.TryGetValue(def, out Entry? entry) ? entry : Missing;

    private CorporateSupplyAnalysis(CorporateTradeConfigDef settings)
    {
        List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
        foreach (ThingDef def in defs)
        {
            entries[def] = new Entry { tech = def.techLevel };
        }

        MarkNaturalProducts(defs);
        MarkTraderSources(defs);
        List<RecipeDef> recipes = WorkTableRecipes();
        MarkCraftable(defs, recipes);
        AssignRecipeTechLevels(recipes);
        MarkRestricted(defs);
        BuildRedemptionGraph(defs, recipes, settings);
    }

    private void MarkNaturalProducts(List<ThingDef> defs)
    {
        foreach (ThingDef def in defs)
        {
            MarkNatural(def.plant?.harvestedThingDef);
            MarkNatural(def.building?.mineableThing);
            if (def.race != null)
            {
                if (def.race.hasMeat)
                {
                    MarkNatural(def.race.meatDef);
                }

                MarkNatural(def.race.leatherDef);
            }

            if ((def.category == ThingCategory.Pawn || def.IsCorpse) && def.butcherProducts != null)
            {
                foreach (ThingDefCountClass product in def.butcherProducts)
                {
                    if (product.count > 0)
                    {
                        MarkNatural(product.thingDef);
                    }
                }
            }

            if (def.comps == null)
            {
                continue;
            }

            foreach (CompProperties props in def.comps)
            {
                if (props is CompProperties_Milkable milk && milk.milkAmount > 0)
                {
                    MarkNatural(milk.milkDef);
                }

                if (props is CompProperties_Shearable wool && wool.woolAmount > 0)
                {
                    MarkNatural(wool.woolDef);
                }
            }
        }
    }

    private void MarkTraderSources(List<ThingDef> defs)
    {
        List<ThingDef> candidates = defs.Where(d => d.category == ThingCategory.Item && d.tradeability.TraderCanSell()).ToList();
        foreach (TraderKindDef trader in DefDatabase<TraderKindDef>.AllDefsListForReading)
        {
            foreach (StockGenerator generator in trader.stockGenerators)
            {
                // 只收购的生成器不是货源；按原版命名约定识别（StockGenerator_BuyExpensiveSimple、BuyTradeTag、BuySingleDef 等）。
                if (generator.GetType().Name.StartsWith("StockGenerator_Buy", StringComparison.Ordinal))
                {
                    continue;
                }

                int failures = 0;
                string? firstFailure = null;
                for (int i = candidates.Count - 1; i >= 0; i--)
                {
                    ThingDef def = candidates[i];
                    try
                    {
                        if (!generator.HandlesThingDef(def))
                        {
                            continue;
                        }

                        entries[def].sourced = true;
                        candidates.RemoveAt(i);
                    }
                    catch (Exception ex)
                    {
                        // 无法识别的供应器不作为来源证据；不尝试实际生成库存。
                        failures++;
                        firstFailure ??= def.defName + ": " + ex.GetType().Name + ": " + ex.Message;
                    }
                }

                if (failures > 0)
                {
                    string generatorName = generator.GetType().FullName ?? generator.GetType().Name;
                    string traderName = trader.defName;
                    int count = failures;
                    string sample = firstFailure ?? string.Empty;
                    MugirlLog.DevMessage(() => "Corporate supply analysis: " + generatorName + " in trader " + traderName
                        + " threw in HandlesThingDef for " + count + " item def(s); they are not counted as sourced by it. First: " + sample);
                }
            }
        }
    }

    private static List<RecipeDef> WorkTableRecipes()
    {
        return DefDatabase<RecipeDef>.AllDefsListForReading.Where(r =>
            r.products != null && r.products.Count > 0 && r.ingredients != null && r.ingredients.Count > 0
            && r.AllRecipeUsers.Any(d => typeof(Building_WorkTable).IsAssignableFrom(d.thingClass))).ToList();
    }

    private void MarkCraftable(List<ThingDef> defs, List<RecipeDef> recipes)
    {
        HashSet<ThingDef> reachable = new HashSet<ThingDef>(defs.Where(d => entries[d].natural || entries[d].sourced));
        // 只有全部原料都能从已知来源获取的配方才能扩展来源图，循环配方不会自行成为起点。
        bool changed;
        do
        {
            changed = false;
            foreach (RecipeDef recipe in recipes)
            {
                if (!recipe.ingredients.All(i => i.filter.AllowedThingDefs.Any(d => reachable.Contains(d)
                    && recipe.fixedIngredientFilter.Allows(d))))
                {
                    continue;
                }

                foreach (ThingDefCountClass product in recipe.products)
                {
                    if (product?.thingDef == null || product.count <= 0)
                    {
                        continue;
                    }

                    entries[product.thingDef].crafted = true;
                    if (reachable.Add(product.thingDef))
                    {
                        changed = true;
                    }
                }
            }
        }
        while (changed);
    }

    private void AssignRecipeTechLevels(List<RecipeDef> recipes)
    {
        foreach (RecipeDef recipe in recipes)
        {
            TechLevel tech = recipe.researchPrerequisite?.techLevel ?? TechLevel.Undefined;
            if (recipe.researchPrerequisites != null)
            {
                foreach (ResearchProjectDef research in recipe.researchPrerequisites)
                {
                    if (research.techLevel > tech)
                    {
                        tech = research.techLevel;
                    }
                }
            }

            foreach (ThingDefCountClass product in recipe.products)
            {
                if (product?.thingDef != null && product.thingDef.techLevel == TechLevel.Undefined
                    && (entries[product.thingDef].tech == TechLevel.Undefined || tech < entries[product.thingDef].tech))
                {
                    entries[product.thingDef].tech = tech;
                }
            }
        }
    }

    private void MarkRestricted(List<ThingDef> defs)
    {
        foreach (ThingDef def in defs)
        {
            if (def.category != ThingCategory.Item)
            {
                continue;
            }

            Entry entry = entries[def];
            bool declaredValue = def.statBases?.Any(s => s.stat == StatDefOf.MarketValue && s.value > 0f) == true;
            bool materialValue = def.MadeFromStuff || def.costList?.Any(c => c.count > 0) == true;
            entry.missingValue = !declaredValue && !materialValue && !entry.natural && !entry.crafted;
            string? opaqueReason = CorporateForeignBehavior.OpaqueBehaviorReason(def);
            if (opaqueReason != null && Prefs.DevMode)
            {
                MugirlLog.DevMessage("Mugirl.CorporatePricing.RestrictionDiagnostic".Translate(def.defName, opaqueReason));
            }

            entry.restricted = entry.missingValue || opaqueReason != null
                || def.comps?.Any(p => p.compClass != null &&
                    (typeof(CompSpawner).IsAssignableFrom(p.compClass)
                    || typeof(CompSpawnerItems).IsAssignableFrom(p.compClass)
                    || typeof(CompHatcher).IsAssignableFrom(p.compClass)
                    || typeof(CompUseEffect_GiveQuest).IsAssignableFrom(p.compClass))) == true;
        }
    }

    private void MarkNatural(ThingDef? def)
    {
        if (def != null && entries.TryGetValue(def, out Entry? entry))
        {
            entry.natural = true;
        }
    }

    private void BuildRedemptionGraph(List<ThingDef> defs, List<RecipeDef> recipes, CorporateTradeConfigDef settings)
    {
        var conversions = new List<Conversion>();
        List<ThingDef> items = defs.Where(d => d.category == ThingCategory.Item).ToList();
        foreach (ThingDef def in items)
        {
            if (def.butcherProducts?.Count > 0)
            {
                conversions.Add(new Conversion { input = def, count = 1, outputs = def.butcherProducts });
            }

            if (def.smeltProducts?.Count > 0)
            {
                conversions.Add(new Conversion { input = def, count = 1, outputs = def.smeltProducts });
            }
        }

        foreach (RecipeDef recipe in recipes)
        {
            // 多种原料的配方不能把全部产出收入归给其中一份便宜副产物。
            if (recipe.ingredients.Count != 1 || recipe.productHasIngredientStuff
                || recipe.specialProducts?.Count > 0)
            {
                continue;
            }

            IngredientCount ingredient = recipe.ingredients[0];
            if (!ingredient.IsFixedIngredient)
            {
                continue;
            }

            ThingDef input = ingredient.FixedIngredient;
            if (input == null || input.category != ThingCategory.Item)
            {
                continue;
            }

            int count = ingredient.CountRequiredOfFor(input, recipe);
            if (count > 0)
            {
                conversions.Add(new Conversion { input = input, count = count, outputs = recipe.products });
            }
        }

        // 使用可兑现价值传播，不层层叠采购利润。最高收购倍率是保守的收入上界。
        decimal saleFactor = Math.Max(1m, Math.Max((decimal)settings.rawProductPriceFactor, (decimal)settings.productPriceFactor));
        var values = items.ToDictionary(d => d,
            d => d == ThingDefOf.Silver ? 1m : Math.Max(0m, CorporateMoney.Value(d.BaseMarketValue)) * saleFactor);
        var changed = new HashSet<ThingDef>();
        for (int pass = 0; pass < 32; pass++)
        {
            changed.Clear();
            var next = new Dictionary<ThingDef, decimal>(values);
            foreach (Conversion conversion in conversions)
            {
                decimal total = 0m;
                foreach (ThingDefCountClass output in conversion.outputs)
                {
                    if (output?.thingDef != null && output.count > 0 && values.TryGetValue(output.thingDef, out decimal value))
                    {
                        total += Math.Min(CorporateMoney.MaxTotal, value * output.count);
                    }
                }

                decimal redeemed = Math.Min(CorporateMoney.MaxTotal, total / conversion.count);
                Entry entry = entries[conversion.input];
                entry.redemption = Math.Max(entry.redemption, redeemed);
                if (redeemed > values[conversion.input] + 0.000001m)
                {
                    next[conversion.input] = Math.Max(next[conversion.input], redeemed);
                    changed.Add(conversion.input);
                }

                if (redeemed >= CorporateMoney.MaxTotal)
                {
                    entry.restricted = true;
                }
            }

            values = next;
            if (changed.Count == 0)
            {
                break;
            }
        }

        // 超过分析深度或持续增值的环路只限制供货，不把物品标成天价。
        foreach (ThingDef def in changed)
        {
            entries[def].restricted = true;
        }

        bool more;
        do
        {
            more = false;
            foreach (Conversion conversion in conversions)
            {
                if (!entries[conversion.input].restricted && conversion.outputs.Any(p => p?.thingDef != null
                    && entries[p.thingDef].restricted))
                {
                    entries[conversion.input].restricted = true;
                    more = true;
                }
            }
        }
        while (more);

        foreach (ThingDef def in items)
        {
            Entry entry = entries[def];
            // 明确的便宜原料若被异常转换配方关联高额收益，限制复制而不是抬高其正常售价。
            if (entry.natural && entry.redemption > Math.Max(1m, CorporateMoney.Value(def.BaseMarketValue)) * 10m)
            {
                entry.restricted = true;
                entry.redemption = 0m;
            }
        }
    }
}
