using HarmonyLib;
using Mugirl.Features.Milk;

namespace Mugirl.Features.Xenotype;

// 出生入口：生成前修正异种，使出生时的渲染树按雪牛娘异种初始化；生成后再检查一次（修正是幂等的），
// 并在成功出生后通知母亲开始泌乳（LactationUtility，属于产奶模块）。
[HarmonyPatch(typeof(PawnUtility), nameof(PawnUtility.TrySpawnHatchedOrBornPawn))]
[MugirlPatch(PatchModule.Xenotype, PatchRisk.High, "Birth-time xenotype correction is skipped; the low-frequency game component remains as fallback.")]
public static class Harmony_PawnUtility_TrySpawnHatchedOrBornPawn_Xenotype
{
    public static void Prefix(Pawn pawn, Thing motherOrEgg)
    {
        XenotypeUtility.ForceFemaleMugirlXenotypeIfNeeded(pawn, birtherThing: motherOrEgg);
    }

    public static void Postfix(Pawn pawn, Thing motherOrEgg, bool __result)
    {
        XenotypeUtility.ForceFemaleMugirlXenotypeIfNeeded(pawn, birtherThing: motherOrEgg);
        if (__result && motherOrEgg is Pawn mother)
        {
            LactationUtility.NotifyBirth(mother);
        }
    }
}
