using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(Pawn_ApparelTracker), "Unlock")]
    public static class Pawn_ApparelTracker_Unlock_Patch
    {
        public static bool Prefix(Apparel apparel)
        {
            if (apparel is Apparel_SlaveApparel slaveApparel)
            {
                if (slaveApparel is AdvancedSlaveApparel advanced && advanced.IsCracked())
                {
                    return true;
                }

                return false;
            }
            return true;
        }
    }
}
