using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(RecipeDefGenerator))]
    [HarmonyPatch("DrugAdministerDefs")]
    public static class Patch_DrugAdministerDefs
    {
        public static void Postfix(ref IEnumerable<RecipeDef> __result, bool hotReload)
        {
            List<RecipeDef> recipes = __result == null ? new List<RecipeDef>() : new List<RecipeDef>(__result);
            ThingDef milkDef = MugirlOptionalDefs.ThingDefs.MugirlMilk;
            if (milkDef == null)
            {
                __result = recipes;
                return;
            }

            if (milkDef.ingestible == null)
            {
                MugirlLog.WarningOnce(
                    "DrugAdministerDefs.MilkNotIngestible",
                    "Skipping generated administer milk recipe because Mugirl_Milk has no ingestible properties.");
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
                RecipeDef recipe = recipes[i];
                if (recipe != null && recipe.defName == defName)
                {
                    return true;
                }
            }

            return false;
        }

        private static RecipeDef CreateAdministerMilkRecipe(ThingDef milkDef, string defName, bool hotReload)
        {
            RecipeDef recipeDef = hotReload ? DefDatabase<RecipeDef>.GetNamedSilentFail(defName) : null;
            if (recipeDef == null)
            {
                recipeDef = new RecipeDef();
            }

            ResetGeneratedRecipe(recipeDef);
            recipeDef.defName = defName;
            recipeDef.label = "RecipeAdminister".Translate(milkDef.label);
            recipeDef.jobString = "RecipeAdministerJobString".Translate(milkDef.label);
            recipeDef.workerClass = typeof(Recipe_AdministerIngestible);
            recipeDef.targetsBodyPart = false;
            recipeDef.anesthetize = false;
            recipeDef.surgerySuccessChanceFactor = 99999f;
            recipeDef.modContentPack = milkDef.modContentPack;
            recipeDef.workAmount = milkDef.ingestible.baseIngestTicks;
            recipeDef.humanlikeOnly = milkDef.ingestible.humanlikeOnly;

            IngredientCount ingredientCount = new IngredientCount();
            ingredientCount.SetBaseCount(1f);
            ingredientCount.filter.SetAllow(milkDef, allow: true);
            recipeDef.ingredients.Add(ingredientCount);
            recipeDef.fixedIngredientFilter.SetAllow(milkDef, allow: true);

            PopulateRecipeUsers(recipeDef);

            return recipeDef;
        }

        private static void PopulateRecipeUsers(RecipeDef recipeDef)
        {
            if (recipeDef.recipeUsers == null)
            {
                recipeDef.recipeUsers = new List<ThingDef>();
            }

            List<ThingDef> pawnDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < pawnDefs.Count; i++)
            {
                ThingDef pawnDef = pawnDefs[i];
                if (pawnDef?.category == ThingCategory.Pawn && pawnDef.race != null && pawnDef.race.IsFlesh)
                {
                    recipeDef.recipeUsers.Add(pawnDef);
                }
            }
        }

        private static void ResetGeneratedRecipe(RecipeDef recipeDef)
        {
            // 热重载可能复用上一次生成的 RecipeDef；填充前先清可变集合，避免每次重载叠加一份牛奶原料。
            if (recipeDef.ingredients == null)
            {
                recipeDef.ingredients = new List<IngredientCount>();
            }
            else
            {
                recipeDef.ingredients.Clear();
            }

            recipeDef.fixedIngredientFilter = new ThingFilter();
            recipeDef.defaultIngredientFilter = null;
            recipeDef.recipeUsers = new List<ThingDef>();
        }

    }
}
