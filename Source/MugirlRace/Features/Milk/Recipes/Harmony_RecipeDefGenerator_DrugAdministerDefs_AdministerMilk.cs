using HarmonyLib;

namespace Mugirl.Features.Milk;

// 为雪牛娘奶生成原版式的“给予”手术配方。生成的 defName “Administer_<奶的 defName>” 被存档中的账单引用，不能改变。
[HarmonyPatch(typeof(RecipeDefGenerator), "DrugAdministerDefs", typeof(bool))]
[MugirlPatch(PatchModule.Milk, PatchRisk.Medium, "The generated administer-milk recipe is not added.")]
public static class Harmony_RecipeDefGenerator_DrugAdministerDefs_AdministerMilk
{
    public static void Postfix(ref IEnumerable<RecipeDef>? __result, bool hotReload)
    {
        List<RecipeDef> recipes = __result is null ? new List<RecipeDef>() : new List<RecipeDef>(__result);
        __result = recipes;
        ThingDef? milkDef = MilkDefOf.Mugirl_Milk;
        if (milkDef is null)
        {
            return;
        }

        if (milkDef.ingestible is null)
        {
            MugirlLog.WarningOnce(
                "DrugAdministerDefs.MilkNotIngestible",
                "Skipping generated administer milk recipe because Mugirl_Milk has no ingestible properties.");
            return;
        }

        string defName = "Administer_" + milkDef.defName;
        if (!ContainsRecipe(recipes, defName))
        {
            recipes.Add(CreateAdministerMilkRecipe(milkDef, defName, hotReload));
        }
    }

    private static bool ContainsRecipe(List<RecipeDef> recipes, string defName)
    {
        for (int i = 0; i < recipes.Count; i++)
        {
            if (recipes[i]?.defName == defName)
            {
                return true;
            }
        }

        return false;
    }

    private static RecipeDef CreateAdministerMilkRecipe(ThingDef milkDef, string defName, bool hotReload)
    {
        // 生成的 Def 没有 DefOf；只在热重载时按名复用上一次生成的对象。
        RecipeDef recipeDef = (hotReload ? DefDatabase<RecipeDef>.GetNamedSilentFail(defName) : null) ?? new RecipeDef();

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

        IngredientCount ingredientCount = new();
        ingredientCount.SetBaseCount(1f);
        ingredientCount.filter.SetAllow(milkDef, allow: true);
        recipeDef.ingredients.Add(ingredientCount);
        recipeDef.fixedIngredientFilter.SetAllow(milkDef, allow: true);

        PopulateRecipeUsers(recipeDef);

        return recipeDef;
    }

    private static void PopulateRecipeUsers(RecipeDef recipeDef)
    {
        recipeDef.recipeUsers ??= new List<ThingDef>();

        List<ThingDef> pawnDefs = DefDatabase<ThingDef>.AllDefsListForReading;
        for (int i = 0; i < pawnDefs.Count; i++)
        {
            ThingDef pawnDef = pawnDefs[i];
            if (pawnDef?.category == ThingCategory.Pawn && pawnDef.race is { IsFlesh: true })
            {
                recipeDef.recipeUsers.Add(pawnDef);
            }
        }
    }

    private static void ResetGeneratedRecipe(RecipeDef recipeDef)
    {
        // 热重载可能复用上一次生成的 RecipeDef；填充前先清可变集合，避免每次重载叠加一份牛奶原料。
        if (recipeDef.ingredients is null)
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
