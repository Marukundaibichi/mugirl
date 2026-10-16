using HarmonyLib;

namespace Mugirl.Features.Incidents;

// 死亡前记录致死伤害是否来自玩家；是否召唤惩罚者由 MapComponent_Migration 在之后的 tick 中结算。
[HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
[MugirlPatch(PatchModule.Incidents, PatchRisk.Low, "Punisher eligibility no longer verifies that the player caused each migration death.")]
internal static class Harmony_Pawn_Kill_MigrationOutcome
{
    public static void Prefix(Pawn __instance, DamageInfo? dinfo)
    {
        if (EventPawnTags.IsMigrationPawn(__instance))
        {
            MapComponent_Migration.NotifyMigrationPawnKilled(__instance, dinfo);
        }
    }
}
