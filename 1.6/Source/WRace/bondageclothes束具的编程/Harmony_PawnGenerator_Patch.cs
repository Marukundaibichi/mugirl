using HarmonyLib;
using System;
using Verse;

namespace MooGirl
{

    [StaticConstructorOnStartup]
    public static class PawnGenerator_GeneratePawn_Patch
    {
        static PawnGenerator_GeneratePawn_Patch()
        {
            MooGirlMod.harmony.Patch(
                AccessTools.Method(typeof(PawnGenerator), "GeneratePawn", new Type[] { typeof(PawnGenerationRequest) }),
                postfix: new HarmonyMethod(typeof(PawnGenerator_GeneratePawn_Patch), nameof(Postfix))
            );
        }

        public static void Postfix(Pawn __result)
        {
            if (__result == null || __result.apparel == null) return;

            foreach (var apparel in __result.apparel.WornApparel)
            {
                if (apparel is SlaveApparel) 
                {
                    __result.apparel.Lock(apparel);
                }
            }
        }
    }
}
