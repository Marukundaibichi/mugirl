using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;
using RimWorld.Planet;

namespace Mugirl
{
    // 巨企支援小队的"死战条款"：特性本体只负责展示与判定，
    // 不撤退与友伤折扣由本文件的两个补丁在原版入口处强制执行。
    internal static class CorporateDiehardUtility
    {
        // 原版 Faction.Notify_MemberTookDamage 的友伤惩罚为 -1.3×伤害(上限100)；
        // 死士条款下仅保留 5%，即关系损失降低 95%。
        public const float FriendlyFireGoodwillFactor = 0.05f;

        public static bool IsDiehard(Pawn pawn)
        {
            return pawn != null && pawn.story?.traits != null
                && pawn.story.traits.HasTrait(MugirlContentDefOf.Mugirl_CorporateDiehard);
        }

        public static void MakeDiehard(Pawn pawn)
        {
            if (pawn?.story?.traits == null) return;
            if (!IsDiehard(pawn)) pawn.story.traits.GainTrait(new Trait(MugirlContentDefOf.Mugirl_CorporateDiehard));
            // 原生双保险：两者都关闭受击触发的个体恐慌逃亡（PanicFlee）。
            pawn.mindState.canFleeIndividual = false;
            pawn.mindState.mentalStateHandler.neverFleeIndividual = true;
        }
    }

    // 拦截所有来源的 PanicFlee（受击、恐惧、心灵能力等）；火焰恐慌(PanicFleeFire)属于求生反应，不在拦截范围。
    [HarmonyPatch(typeof(MentalStateHandler), nameof(MentalStateHandler.TryStartMentalState))]
    internal static class Harmony_CorporateDiehard_NoPanicFlee
    {
        internal static bool Prefix(Pawn ___pawn, MentalStateDef stateDef)
        {
            return !(stateDef == MentalStateDefOf.PanicFlee && CorporateDiehardUtility.IsDiehard(___pawn));
        }
    }

    // 原版在 Faction.Notify_MemberTookDamage 中以 AttackedMember 事件结算玩家友伤好感惩罚；
    // 在统一出口 TryAffectGoodwillWith 处把死士成员的惩罚缩至 5%，消息数值同步缩减。
    [HarmonyPatch(typeof(Faction), nameof(Faction.TryAffectGoodwillWith))]
    internal static class Harmony_CorporateDiehard_FriendlyFireGoodwill
    {
        internal static void Prefix(Faction __instance, Faction other, ref int goodwillChange,
            HistoryEventDef reason, LookTargets lookTarget)
        {
            Faction playerFaction = Faction.OfPlayerSilentFail;
            if (goodwillChange >= 0 || reason != HistoryEventDefOf.AttackedMember || other == null
                || playerFaction == null || __instance != playerFaction || lookTarget == null)
            {
                return;
            }
            foreach (GlobalTargetInfo target in lookTarget.targets)
            {
                if (target.Thing is Pawn pawn && CorporateDiehardUtility.IsDiehard(pawn))
                {
                    goodwillChange = Mathf.RoundToInt(goodwillChange * CorporateDiehardUtility.FriendlyFireGoodwillFactor);
                    return;
                }
            }
        }
    }
}
