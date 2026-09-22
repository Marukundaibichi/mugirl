using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    internal static class MugirlPregnancyMoodUtility
    {
        internal static void EnsurePositiveThought(HediffComp_GiveRandomSituationalThought comp)
        {
            if (!ModsConfig.BiotechActive
                || comp?.parent?.pawn == null
                || comp.parent.def != HediffDefOf.PregnancyMood
                || !MugirlIdentity.IsMugirlPawn(comp.parent.pawn)
                || IsPositive(comp.selectedThought))
            {
                return;
            }

            // 沿用 Def 的正面心情及数值，不修改共享候选列表或其他种族的抽取结果。
            var thoughts = comp.Props?.thoughtDefs;
            if (thoughts == null)
            {
                return;
            }

            ThoughtDef replacement = null;
            int positiveCount = 0;
            for (int i = 0; i < thoughts.Count; i++)
            {
                if (IsPositive(thoughts[i]))
                {
                    positiveCount++;
                    if (Rand.RangeInclusive(1, positiveCount) == 1)
                    {
                        replacement = thoughts[i];
                    }
                }
            }

            // 其他 mod 移除全部正面候选时保留原状态，避免生成无效 Thought。
            if (replacement != null)
            {
                comp.selectedThought = replacement;
            }
        }

        private static bool IsPositive(ThoughtDef thought)
        {
            return thought?.stages != null
                && thought.stages.Count > 0
                && thought.stages[0].baseMoodEffect > 0f;
        }
    }

    [HarmonyPatch(typeof(HediffComp_GiveRandomSituationalThought), nameof(HediffComp_GiveRandomSituationalThought.CompPostMake))]
    internal static class Harmony_MugirlPregnancyMood_New
    {
        private static void Postfix(HediffComp_GiveRandomSituationalThought __instance)
        {
            MugirlPregnancyMoodUtility.EnsurePositiveThought(__instance);
        }
    }

    [HarmonyPatch(typeof(HediffComp_GiveRandomSituationalThought), nameof(HediffComp_GiveRandomSituationalThought.CompExposeData))]
    internal static class Harmony_MugirlPregnancyMood_Load
    {
        private static void Postfix(HediffComp_GiveRandomSituationalThought __instance)
        {
            // 读档完成后再转换旧负面状态；保留既有正面状态及剩余持续时间。
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                MugirlPregnancyMoodUtility.EnsurePositiveThought(__instance);
            }
        }
    }
}
