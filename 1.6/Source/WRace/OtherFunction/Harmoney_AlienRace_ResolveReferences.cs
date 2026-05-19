//using System;
//using AlienRace;
//using System.Collections.Generic;
//using HarmonyLib;
//using RimWorld;
//using Verse;

//namespace MooGirl
//{
//    [HarmonyPatch(typeof(ThingDef_AlienRace))]
//    public static class ThingDef_AlienRace_ResolveReferences_Patch
//    {
//        // patch 编译器生成的 lambda <ResolveReferences>b__1_2
//        [HarmonyPatch("<ResolveReferences>b__1_2")]
//        [HarmonyPrefix]
//        private static bool ResolveReferences_Prefix(ThingDef_AlienRace __instance, RecipeDef rd)
//        {
//            List<ThingDef> recipeUsers = rd.recipeUsers;
//            if (recipeUsers != null && recipeUsers.Contains(ThingDefOf.Human))
//            {
//                rd.recipeUsers.Add(__instance);
//            }

//            ThingFilter defaultIngredientFilter = rd.defaultIngredientFilter;
//            if (defaultIngredientFilter != null && !defaultIngredientFilter.Allows(ThingDefOf.Meat_Human))
//            {
//                // 如果 meatDef 是牛肉、羊肉或其他非人肉类型，则允许
//                ThingDef meatDef = __instance.race.meatDef;

//                if (meatDef == ThingDef.Named("Meat_Muffalo"))
//                {
//                    rd.defaultIngredientFilter.SetAllow(meatDef, true);
//                }
//            }

//            // 禁用原 lambda 执行
//            return false;
//        }
//    }
//}
