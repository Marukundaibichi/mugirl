using HarmonyLib;
using RimWorld;
using Verse;

namespace Mugirl
{
    // 怀孕变体服装（apparel.tags 含 MugirlPregnantSwap，如金色比基尼）：
    // 穿戴者怀孕期间穿着贴图路径追加 _Pregnant（形如 ..._Pregnant_Female_east），
    // 使用画师提供的怀孕变体贴图；分娩、流产或终止妊娠（PregnancyUtility 走 RemoveHediff）后换回原图。
    internal static class MugirlPregnantApparelUtility
    {
        internal const string Tag = "MugirlPregnantSwap";
        internal const string VariantSuffix = "_Pregnant";

        internal static bool IsSwapApparel(Apparel apparel)
        {
            System.Collections.Generic.List<string> tags = apparel?.def?.apparel?.tags;
            return tags != null && tags.Contains(Tag);
        }

        internal static bool IsPregnant(Pawn pawn)
        {
            return pawn != null
                && HediffDefOf.PregnantHuman != null
                && pawn.health?.hediffSet != null
                && pawn.health.hediffSet.HasHediff(HediffDefOf.PregnantHuman);
        }

        // 穿着贴图在图形重建时才重新读取路径，这里先立即脏化；雪牛娘再走既有延后刷新兜底，
        // 覆盖 HAR 渲染树重建滞后的情况。非雪牛娘穿戴者只依赖立即脏化。
        internal static void NotifyPregnancyChanged(Pawn pawn)
        {
            if (pawn == null || pawn.Destroyed)
            {
                return;
            }

            pawn.Drawer?.renderer?.SetAllGraphicsDirty();
            if (MountedPawnUtility.IsMugirl(pawn))
            {
                PawnRenderingRefreshUtility.NotifyPawnChanged(pawn);
            }
        }
    }

    // 只改读取路径；Apparel.WornGraphicPath 在原版中仅被 ApparelGraphicRecordGetter.TryGetGraphicApparel 消费，
    // 追加后缀会自然组合成 ..._Pregnant_<BodyType>_<Rot> 的贴图文件名。
    [HarmonyPatch(typeof(Apparel), "WornGraphicPath", MethodType.Getter)]
    public static class Harmony_MugirlPregnantApparel_WornGraphicPath
    {
        public static void Postfix(Apparel __instance, ref string __result)
        {
            if (__result.NullOrEmpty() || !MugirlPregnantApparelUtility.IsSwapApparel(__instance))
            {
                return;
            }

            if (!MugirlPregnantApparelUtility.IsPregnant(__instance.Wearer))
            {
                return;
            }

            __result += MugirlPregnantApparelUtility.VariantSuffix;
        }
    }

    // 受孕、移植胚胎等使怀孕 Hediff 新增时刷新穿着图形。
    [HarmonyPatch(typeof(Hediff), nameof(Hediff.PostAdd))]
    public static class Harmony_MugirlPregnantApparel_PostAdd
    {
        public static void Postfix(Hediff __instance)
        {
            if (HediffDefOf.PregnantHuman == null || __instance?.def != HediffDefOf.PregnantHuman)
            {
                return;
            }

            MugirlPregnantApparelUtility.NotifyPregnancyChanged(__instance.pawn);
        }
    }

    // 分娩、流产与终止妊娠都会经 Pawn_HealthTracker.RemoveHediff 移除怀孕 Hediff。
    [HarmonyPatch(typeof(Pawn_HealthTracker), nameof(Pawn_HealthTracker.RemoveHediff))]
    public static class Harmony_MugirlPregnantApparel_RemoveHediff
    {
        public static void Postfix(Hediff hediff)
        {
            if (HediffDefOf.PregnantHuman == null || hediff?.def != HediffDefOf.PregnantHuman)
            {
                return;
            }

            MugirlPregnantApparelUtility.NotifyPregnancyChanged(hediff.pawn);
        }
    }
}
