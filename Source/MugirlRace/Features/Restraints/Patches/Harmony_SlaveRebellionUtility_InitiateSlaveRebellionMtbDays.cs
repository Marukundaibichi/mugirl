using HarmonyLib;

namespace Mugirl.Features.Restraints;

// 洗脑服从特质的奴隶永不发起叛乱：返回原版对非奴隶使用的 -1（不参与叛乱）。
[HarmonyPatch(typeof(SlaveRebellionUtility), nameof(SlaveRebellionUtility.InitiateSlaveRebellionMtbDays))]
[MugirlPatch(PatchModule.Restraints, PatchRisk.High, "Brainwash-obey pawns use the base rebellion MTB calculation.")]
public static class Harmony_SlaveRebellionUtility_InitiateSlaveRebellionMtbDays
{
    public static bool Prefix(Pawn pawn, ref float __result)
    {
        if (pawn.story?.traits.HasTrait(RestraintsDefOf.Mugirl_BrainwashObey) == true)
        {
            __result = -1f;
            return false;
        }

        return true;
    }
}
