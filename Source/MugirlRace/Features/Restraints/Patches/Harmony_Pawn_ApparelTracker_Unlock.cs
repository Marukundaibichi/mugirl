using HarmonyLib;

namespace Mugirl.Features.Restraints;

// Pawn_ApparelTracker 上保护锁定束具的补丁。类名沿用 1.6（HarmonyBootstrap 按完整类名排序应用补丁）。

// 原版自动解锁路径（耐久低于 50% 时 ApparelTrackerTickInterval、GenGuest.TryEnslavePrisoner 的 UnlockAll）
// 都经过 Unlock；仍锁定且穿着的束具跳过原方法，保持与 isLocked 一致。钥匙与破解流程会先清除 isLocked 再调用 Unlock。
[HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Unlock))]
[MugirlPatch(PatchModule.Restraints, PatchRisk.High, "Base auto-unlock paths can desynchronize worn slave apparel from its saved lock state.")]
public static class Harmony_Pawn_ApparelTracker_Unlock
{
    public static bool Prefix(Pawn_ApparelTracker __instance, Apparel apparel)
    {
        return !apparel.IsLockedRestraint()
            || __instance?.WornApparel?.Contains(apparel) != true;
    }
}
