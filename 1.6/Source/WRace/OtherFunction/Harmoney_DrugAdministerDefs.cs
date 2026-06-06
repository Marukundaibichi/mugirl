using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using System.Linq;

namespace MooGirl
{
    [HarmonyPatch(typeof(RecipeDefGenerator))]
    [HarmonyPatch("DrugAdministerDefs")]
    public static class Patch_DrugAdministerDefs
    {
        // Postfix 用于在原方法执行后额外添加配方
        public static void Postfix(ref IEnumerable<RecipeDef> __result, bool hotReload)
        {
            // 获取原结果转成列表方便操作
            var list = __result.ToList();

            // 查找 ThingDef
            ThingDef milkDef = MooGirlOptionalDefs.ThingDefs.MooGirlMilk;
            if (milkDef != null)
            {
                string defName = "Administer_" + milkDef.defName;

                // 检查是否已经生成过，避免重复
                if (!list.Any(r => r.defName == defName))
                {
                    RecipeDef recipeDef = hotReload
                        ? (DefDatabase<RecipeDef>.GetNamed(defName, false) ?? new RecipeDef())
                        : new RecipeDef();

                    recipeDef.defName = defName;
                    recipeDef.label = "RecipeAdminister".Translate(milkDef.label);
                    recipeDef.jobString = "RecipeAdministerJobString".Translate(milkDef.label);
                    recipeDef.workerClass = typeof(Recipe_AdministerIngestible);
                    recipeDef.targetsBodyPart = false;
                    recipeDef.anesthetize = false;
                    recipeDef.surgerySuccessChanceFactor = 99999f;
                    recipeDef.modContentPack = milkDef.modContentPack;
                    recipeDef.workAmount = milkDef.ingestible?.baseIngestTicks ?? 250;
                    recipeDef.humanlikeOnly = milkDef.ingestible?.humanlikeOnly ?? true;

                    // 原料设置
                    IngredientCount ingredientCount = new IngredientCount();
                    ingredientCount.SetBaseCount(1f);
                    ingredientCount.filter.SetAllow(milkDef, allow: true);
                    recipeDef.ingredients.Add(ingredientCount);
                    recipeDef.fixedIngredientFilter.SetAllow(milkDef, allow: true);

                    // 施用对象
                    recipeDef.recipeUsers = new List<ThingDef>();
                    foreach (ThingDef pawnDef in DefDatabase<ThingDef>.AllDefs
                                 .Where(d => d.category == ThingCategory.Pawn && d.race.IsFlesh))
                    {
                        recipeDef.recipeUsers.Add(pawnDef);
                    }

                    // 添加到结果列表
                    list.Add(recipeDef);
                }
            }

            // 更新返回值
            __result = list;
        }
    }
}
