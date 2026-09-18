using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Verse;

namespace Mugirl
{
    // PatchGovernance:
    // Targets: 原版 IncidentWorker_FarmAnimalsWanderIn.TryFindRandomPawnKind 与
    //          StockGenerator_Animals.PawnKindAllowed 两处对 PawnKindDef.race/RaceProps 的裸解引用。
    // Scope: 当 DefDatabase 中存在 race 未解析（null）的 PawnKindDef 时，这两个方法每次被调用都会抛
    //        NullReferenceException：前者让 storyteller 的事件筛选每轮报错，后者在据点进货时直接
    //        中断 TraderStock 生成（RegenerateStock 已先销毁旧库存），表现为据点商品和白银全部清空。
    //        Finalizer 只在原方法抛异常时兜底返回 false（跳过该判定/该 kind），正常结果不受影响。
    // Duplicate guard: 不改写原方法逻辑、不与其它补丁竞争返回值；异常路径才会介入。
    // Failure: 目标方法改名时补丁由 MugirlBootstrap 记录为注册失败，行为回退到原版。
    internal static class MugirlNullRacePawnKindGuard
    {
        // StaticCacheLifecycle: 进程级一次性诊断；只在实际捕获到异常时才扫描输出，健康的对局零开销。
        private static bool reportedBrokenDefs;

        internal static void ReportBrokenPawnKindDefsOnce(string context)
        {
            if (reportedBrokenDefs)
            {
                return;
            }

            reportedBrokenDefs = true;
            List<string> broken = DefDatabase<PawnKindDef>.AllDefs
                .Where(k => k.race == null)
                .Select(k => k.defName)
                .ToList();
            if (broken.Count == 0)
            {
                broken.Add("(no PawnKindDef with null race found; the exception came from another source)");
            }

            StringBuilder stringBuilder = new StringBuilder();
            stringBuilder.Append("Some PawnKindDef(s) have an unresolved race reference and were skipped (context: ")
                .Append(context).Append("): ");
            stringBuilder.AppendLine(string.Join(", ", broken));
            stringBuilder.Append("A def like this breaks vanilla FarmAnimalsWanderIn and animal stock generation ");
            stringBuilder.Append("(settlement stock can be wiped including silver). Remove or fix the mod that added these defs.");
            MugirlLog.WarningOnce("NullRacePawnKindGuard.BrokenDefs", stringBuilder.ToString());
        }
    }

    [HarmonyPatch(typeof(IncidentWorker_FarmAnimalsWanderIn), "TryFindRandomPawnKind")]
    internal static class Harmony_FarmAnimalsWanderIn_TryFindRandomPawnKind_NullRaceGuard
    {
        private static Exception Finalizer(Exception __exception, ref bool __result)
        {
            if (__exception == null)
            {
                return null;
            }

            MugirlNullRacePawnKindGuard.ReportBrokenPawnKindDefsOnce("FarmAnimalsWanderIn.CanFireNow");
            __result = false;
            return null;
        }
    }

    [HarmonyPatch(typeof(StockGenerator_Animals), "PawnKindAllowed")]
    internal static class Harmony_StockGenerator_Animals_PawnKindAllowed_NullRaceGuard
    {
        private static Exception Finalizer(Exception __exception, ref bool __result)
        {
            if (__exception == null)
            {
                return null;
            }

            MugirlNullRacePawnKindGuard.ReportBrokenPawnKindDefsOnce("StockGenerator_Animals.PawnKindAllowed");
            __result = false;
            return null;
        }
    }
}
