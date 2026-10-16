using HarmonyLib;

namespace Mugirl.Features.Corporate;

// PatchGovernance: 只记录开户前可证实的攻击责任，送货员单独处理；不改伤害与吸收结果。
[HarmonyPatch(typeof(Pawn), nameof(Pawn.PreApplyDamage))]
[MugirlPatch(PatchModule.Corporate, PatchRisk.Medium, "Introduction dialogue uses incomplete courier and corporate aggression evidence.")]
internal static class Harmony_Pawn_PreApplyDamage_CorporateIntroduction
{
    private static void Prefix(Pawn __instance, ref DamageInfo dinfo)
    {
        CorporateIntroduction.Current?.NotifyDamage(__instance, dinfo);
    }
}
