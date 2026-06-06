using HarmonyLib;
using System;
using Verse;

namespace MooGirl
{

    public static class PawnGenerator_GeneratePawn_Patch
    {
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
