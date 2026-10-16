using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace Mugirl
{
    // 每个游戏组件持有一份索引，新档/读档重建；不在界面或 tick 中扫描全部 Def。
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
            internal ThingDef input;
            internal int count;
            internal List<ThingDefCountClass> outputs;
        }

        private readonly Dictionary<ThingDef, Entry> entries = new Dictionary<ThingDef, Entry>();

        internal Entry Get(ThingDef def)
        {
            return def != null && entries.TryGetValue(def, out Entry entry) ? entry
                : new Entry { restricted = true, missingValue = true };
        }

        internal CorporateSupplyAnalysis(CorporateTradeSettingsDef settings)
        {
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            foreach (ThingDef def in defs) entries[def] = new Entry { tech = def.techLevel };
            foreach (ThingDef def in defs)
            {
                MarkNatural(def.plant?.harvestedThingDef);
                MarkNatural(def.building?.mineableThing);
                if (def.race != null)
                {
                    if (def.race.hasMeat) MarkNatural(def.race.meatDef);
                    MarkNatural(def.race.leatherDef);
                }
                if (def.category == ThingCategory.Pawn || def.IsCorpse)
                    foreach (ThingDefCountClass product in def.butcherProducts ?? Enumerable.Empty<ThingDefCountClass>())
                        if (product.count > 0) MarkNatural(product.thingDef);
                foreach (CompProperties props in def.comps ?? Enumerable.Empty<CompProperties>())
                {
                    if (props is CompProperties_Milkable milk && milk.milkAmount > 0) MarkNatural(milk.milkDef);
                    if (props is CompProperties_Shearable wool && wool.woolAmount > 0) MarkNatural(wool.woolDef);
                }
            }

            List<ThingDef> candidates = defs.Where(d => d.category == ThingCategory.Item
                && d.tradeability.TraderCanSell()).ToList();
            foreach (TraderKindDef trader in DefDatabase<TraderKindDef>.AllDefs)
                foreach (StockGenerator generator in trader.stockGenerators)
                {
                    if (generator.GetType().Name.StartsWith("StockGenerator_Buy", StringComparison.Ordinal)) continue;
                    for (int i = candidates.Count - 1; i >= 0; i--)
                    {
                        ThingDef def = candidates[i];
                        try
                        {
                            if (!generator.HandlesThingDef(def)) continue;
                            entries[def].sourced = true;
                            candidates.RemoveAt(i);
                        }
                        catch (Exception)
                        {
                            // 无法识别的供应器不作为来源证据；不尝试实际生成库存。
                        }
                    }
                }

            List<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading.Where(r =>
                r.products != null && r.products.Count > 0 && r.ingredients != null && r.ingredients.Count > 0
                && r.AllRecipeUsers.Any(d => typeof(Building_WorkTable).IsAssignableFrom(d.thingClass))).ToList();
            HashSet<ThingDef> reachable = new HashSet<ThingDef>(defs.Where(d => entries[d].natural || entries[d].sourced));
            // 只有全部原料都能从已知来源获取的配方才能扩展来源图，循环配方不会自行成为起点。
            bool changed;
            do
            {
                changed = false;
                foreach (RecipeDef recipe in recipes)
                {
                    if (!recipe.ingredients.All(i => i.filter.AllowedThingDefs.Any(d => reachable.Contains(d)
                        && recipe.fixedIngredientFilter.Allows(d)))) continue;
                    foreach (ThingDefCountClass product in recipe.products)
                    {
                        if (product?.thingDef == null || product.count <= 0) continue;
                        entries[product.thingDef].crafted = true;
                        if (reachable.Add(product.thingDef)) changed = true;
                    }
                }
            } while (changed);

            foreach (RecipeDef recipe in recipes)
            {
                TechLevel tech = recipe.researchPrerequisite?.techLevel ?? TechLevel.Undefined;
                foreach (ResearchProjectDef research in recipe.researchPrerequisites ?? Enumerable.Empty<ResearchProjectDef>())
                    if (research.techLevel > tech) tech = research.techLevel;
                foreach (ThingDefCountClass product in recipe.products)
                    if (product?.thingDef != null && product.thingDef.techLevel == TechLevel.Undefined
                        && (entries[product.thingDef].tech == TechLevel.Undefined || tech < entries[product.thingDef].tech))
                        entries[product.thingDef].tech = tech;
            }

            foreach (ThingDef def in defs.Where(d => d.category == ThingCategory.Item))
            {
                Entry entry = entries[def];
                bool declaredValue = def.statBases?.Any(s => s.stat == StatDefOf.MarketValue && s.value > 0f) == true;
                bool materialValue = def.MadeFromStuff || def.costList?.Any(c => c.count > 0) == true;
                entry.missingValue = !declaredValue && !materialValue && !entry.natural && !entry.crafted;
                string opaqueReason = CorporatePricingCompatibility.OpaqueBehaviorReason(def);
                if (opaqueReason != null && Prefs.DevMode)
                    MugirlLog.DevMessage("Mugirl.CorporatePricing.RestrictionDiagnostic".Translate(def.defName, opaqueReason));
                entry.restricted = entry.missingValue || opaqueReason != null
                    || def.comps?.Any(p => p.compClass != null &&
                        (typeof(CompSpawner).IsAssignableFrom(p.compClass)
                        || typeof(CompSpawnerItems).IsAssignableFrom(p.compClass)
                        || typeof(CompHatcher).IsAssignableFrom(p.compClass)
                        || typeof(CompUseEffect_GiveQuest).IsAssignableFrom(p.compClass))) == true;
            }
            BuildRedemptionGraph(defs, recipes, settings);
        }

        private void MarkNatural(ThingDef def)
        {
            if (def != null && entries.TryGetValue(def, out Entry entry)) entry.natural = true;
        }

        private void BuildRedemptionGraph(List<ThingDef> defs, List<RecipeDef> recipes, CorporateTradeSettingsDef settings)
        {
            var conversions = new List<Conversion>();
            foreach (ThingDef def in defs.Where(d => d.category == ThingCategory.Item))
            {
                if (def.butcherProducts?.Count > 0)
                    conversions.Add(new Conversion { input = def, count = 1, outputs = def.butcherProducts });
                if (def.smeltProducts?.Count > 0)
                    conversions.Add(new Conversion { input = def, count = 1, outputs = def.smeltProducts });
            }
            foreach (RecipeDef recipe in recipes)
            {
                // 多种原料的配方不能把全部产出收入归给其中一份便宜副产物。
                if (recipe.ingredients.Count != 1 || recipe.productHasIngredientStuff
                    || recipe.specialProducts?.Count > 0) continue;
                IngredientCount ingredient = recipe.ingredients[0];
                if (!ingredient.IsFixedIngredient) continue;
                ThingDef input = ingredient.FixedIngredient;
                if (input == null || input.category != ThingCategory.Item) continue;
                int count = ingredient.CountRequiredOfFor(input, recipe);
                if (count > 0) conversions.Add(new Conversion { input = input, count = count, outputs = recipe.products });
            }

            // 使用可兑现价值传播，不层层叠采购利润。最高收购倍率是保守的收入上界。
            decimal saleFactor = Math.Max(1m, Math.Max((decimal)settings.rawProductPriceFactor, (decimal)settings.productPriceFactor));
            var values = defs.Where(d => d.category == ThingCategory.Item).ToDictionary(d => d,
                d => d == ThingDefOf.Silver ? 1m : Math.Max(0m, MoneyValue(d.BaseMarketValue)) * saleFactor);
            var changed = new HashSet<ThingDef>();
            for (int pass = 0; pass < 32; pass++)
            {
                changed.Clear();
                var next = new Dictionary<ThingDef, decimal>(values);
                foreach (Conversion conversion in conversions)
                {
                    decimal total = 0m;
                    foreach (ThingDefCountClass output in conversion.outputs)
                        if (output?.thingDef != null && output.count > 0 && values.TryGetValue(output.thingDef, out decimal value))
                            total += Math.Min(100000000m, value * output.count);
                    decimal redeemed = Math.Min(100000000m, total / conversion.count);
                    Entry entry = entries[conversion.input];
                    entry.redemption = Math.Max(entry.redemption, redeemed);
                    if (redeemed > values[conversion.input] + 0.000001m)
                    {
                        next[conversion.input] = Math.Max(next[conversion.input], redeemed);
                        changed.Add(conversion.input);
                    }
                    if (redeemed >= 100000000m) entry.restricted = true;
                }
                values = next;
                if (changed.Count == 0) break;
            }
            // 超过分析深度或持续增值的环路只限制供货，不把物品标成天价。
            foreach (ThingDef def in changed) entries[def].restricted = true;
            bool more;
            do
            {
                more = false;
                foreach (Conversion conversion in conversions)
                    if (!entries[conversion.input].restricted && conversion.outputs.Any(p => p?.thingDef != null
                        && entries[p.thingDef].restricted))
                    { entries[conversion.input].restricted = true; more = true; }
            } while (more);
            foreach (ThingDef def in defs.Where(d => d.category == ThingCategory.Item))
            {
                Entry entry = entries[def];
                // 明确的便宜原料若被异常转换配方关联高额收益，限制复制而不是抬高其正常售价。
                if (entry.natural && entry.redemption > Math.Max(1m, MoneyValue(def.BaseMarketValue)) * 10m)
                { entry.restricted = true; entry.redemption = 0m; }
            }
        }

        internal static decimal MoneyValue(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) || value <= 0f ? 0m : (decimal)Math.Min(value, 100000000f);
        }
    }
}
