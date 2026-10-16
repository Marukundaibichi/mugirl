using HarmonyLib;

namespace Mugirl.Features.Roping;

// 跟随牵引者的雪牛娘可以打开任何门（门本身拒绝时也放行），避免被牵着时卡在门口。
// 虚方法补丁：Building_JammedDoor 的重写不调用基类，不经过这里（与 1.6 相同）。
[HarmonyPatch(typeof(Building_Door), nameof(Building_Door.PawnCanOpen))]
[MugirlPatch(PatchModule.Roping, PatchRisk.Medium, "Roped followers use the base door-opening permission.")]
public static class Harmony_Building_Door_PawnCanOpen
{
    [HarmonyPostfix]
    public static void AllowFollowRoperJob(Pawn p, ref bool __result)
    {
        if (!__result && RopingService.IsFollowingRoper(p))
        {
            __result = true;
        }
    }
}
