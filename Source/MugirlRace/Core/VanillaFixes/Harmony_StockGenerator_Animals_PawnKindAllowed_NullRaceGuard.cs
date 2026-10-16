using HarmonyLib;

namespace Mugirl.Core;

[HarmonyPatch(typeof(StockGenerator_Animals), "PawnKindAllowed", new[] { typeof(PawnKindDef), typeof(RimWorld.Planet.PlanetTile) })]
[MugirlPatch(PatchModule.VanillaFixes, PatchRisk.Medium, "Null-race PawnKindDefs throw inside animal stock generation and can wipe settlement stock.")]
internal static class Harmony_StockGenerator_Animals_PawnKindAllowed_NullRaceGuard
{
    private static Exception? Finalizer(Exception? __exception, ref bool __result)
    {
        if (__exception == null)
        {
            return null;
        }

        NullRacePawnKindGuard.ReportBrokenPawnKindDefsOnce("StockGenerator_Animals.PawnKindAllowed");
        __result = false;
        return null;
    }
}
