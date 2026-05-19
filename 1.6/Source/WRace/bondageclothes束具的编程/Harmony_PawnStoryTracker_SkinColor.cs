using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
using MooGirl;

namespace MooGirlSkinFix
{
    public class GameComponent_MooGirlSkinOnce : GameComponent
    {
        public bool applied = false;

        public GameComponent_MooGirlSkinOnce(Game game)
        {
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref applied, "MooGirlSkinApplied", false);
        }
    }

    [HarmonyPatch(typeof(Pawn_StoryTracker), nameof(Pawn_StoryTracker.SkinColor), MethodType.Getter)]
    public static class Patch_PawnStoryTracker_SkinColor
    {
        private static readonly Color MooGirlSkinColor =
            new Color32(255, 241, 231, 255);

        public static void Postfix(
            Pawn_StoryTracker __instance,
            Pawn ___pawn,
            ref Color __result)
        {
            if (___pawn == null) return;
            if (___pawn.RaceProps == null) return;

            // 只处理雪牛娘
            if (___pawn.RaceProps.body != MooGirl_DefOf.MooGirlBody)
                return;

            // 取 GameComponent
            GameComponent_MooGirlSkinOnce comp =
                Current.Game?.GetComponent<GameComponent_MooGirlSkinOnce>();

            if (comp == null) return;

            // 已经执行过 → 直接放行
            if (comp.applied)
                return;

            // 第一次：强制覆盖颜色
            __result = MooGirlSkinColor;

            // 标记为已执行（对整局游戏）
            comp.applied = true;
        }
    }
}
