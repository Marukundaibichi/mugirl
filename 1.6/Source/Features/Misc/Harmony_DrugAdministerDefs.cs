using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(RecipeDefGenerator))]
    [HarmonyPatch("DrugAdministerDefs")]
    public static class Patch_DrugAdministerDefs
    {
        public static void Postfix(ref IEnumerable<RecipeDef> __result, bool hotReload)
        {
            List<RecipeDef> recipes = new List<RecipeDef>(__result);
            ThingDef milkDef = MooGirlOptionalDefs.ThingDefs.MooGirlMilk;
            if (milkDef == null)
            {
                __result = recipes;
                return;
            }

            string defName = "Administer_" + milkDef.defName;
            if (!ContainsRecipe(recipes, defName))
            {
                recipes.Add(CreateAdministerMilkRecipe(milkDef, defName, hotReload));
            }

            __result = recipes;
        }

        private static bool ContainsRecipe(List<RecipeDef> recipes, string defName)
        {
            for (int i = 0; i < recipes.Count; i++)
            {
                if (recipes[i].defName == defName)
                {
                    return true;
                }
            }

            return false;
        }

        private static RecipeDef CreateAdministerMilkRecipe(ThingDef milkDef, string defName, bool hotReload)
        {
            RecipeDef recipeDef = hotReload
                ? DefDatabase<RecipeDef>.GetNamed(defName, false) ?? new RecipeDef()
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

            IngredientCount ingredientCount = new IngredientCount();
            ingredientCount.SetBaseCount(1f);
            ingredientCount.filter.SetAllow(milkDef, allow: true);
            recipeDef.ingredients.Add(ingredientCount);
            recipeDef.fixedIngredientFilter.SetAllow(milkDef, allow: true);

            recipeDef.recipeUsers = new List<ThingDef>();
            foreach (ThingDef pawnDef in DefDatabase<ThingDef>.AllDefs)
            {
                if (IsFleshPawnDef(pawnDef))
                {
                    recipeDef.recipeUsers.Add(pawnDef);
                }
            }

            return recipeDef;
        }

        private static bool IsFleshPawnDef(ThingDef thingDef)
        {
            return thingDef.category == ThingCategory.Pawn && thingDef.race != null && thingDef.race.IsFlesh;
        }
    }
}
