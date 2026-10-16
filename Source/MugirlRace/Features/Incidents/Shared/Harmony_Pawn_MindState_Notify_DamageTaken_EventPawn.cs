using HarmonyLib;
using Verse.AI;

namespace Mugirl.Features.Incidents;

// 受伤通知：迁徙雪牛娘受击后全体离开，投资人受击后终止交易；被动事件对象跳过原版的狂暴/逃跑反应。
// 跳过原方法也会跳过 lastHarmTick 等记录与 mentalStateHandler 通知（与 1.6 相同）；改为只补丁几率函数需要游戏内验证。
[HarmonyPatch(typeof(Pawn_MindState), nameof(Pawn_MindState.Notify_DamageTaken))]
[MugirlPatch(PatchModule.Incidents, PatchRisk.Medium, "Passive event pawns use the base damage reaction path.")]
internal static class Harmony_Pawn_MindState_Notify_DamageTaken_EventPawn
{
    public static bool Prefix(Pawn_MindState __instance)
    {
        Pawn pawn = __instance.pawn;
        // 绝大多数 Pawn 没有 questTags，单次扫描后直接放行。
        EventPawnFlags flags = EventPawnTags.GetEventFlags(pawn);
        if (flags == EventPawnFlags.None)
        {
            return true;
        }

        if ((flags & EventPawnFlags.Migration) != 0)
        {
            MapComponent_Migration.NotifyMigrationPawnAttacked(pawn);
        }
        else if ((flags & EventPawnFlags.FusionInvestor) != 0)
        {
            FusionInvestmentUtility.NotifyInvestorAttacked(pawn);
        }

        return (flags & EventPawnFlags.Passive) == 0;
    }
}
