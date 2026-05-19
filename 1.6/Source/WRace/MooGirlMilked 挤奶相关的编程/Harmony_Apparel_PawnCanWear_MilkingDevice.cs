using HarmonyLib;
using RimWorld;
using Verse;

namespace MooGirl
{
    [HarmonyPatch(typeof(Apparel), nameof(Apparel.PawnCanWear))]
    public static class Harmony_Apparel_PawnCanWear_MilkingDevice
    {
        public static void Postfix(Apparel __instance, Pawn pawn, ref bool __result)
        {
            if (!__result || !IsMilkingDevice(__instance?.def))
            {
                return;
            }

            __result = pawn?.def == MooGirl_DefOf.MooGirl;
        }

        private static bool IsMilkingDevice(ThingDef def)
        {
            return def != null
                && (def.defName == "MooGirl_MilkingDevice"
                    || def.defName == "MooGirl_MilkingDeviceHidden");
        }
    }
}
