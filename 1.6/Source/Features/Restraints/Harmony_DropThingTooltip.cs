using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;
using Verse.AI;

namespace Mugirl
{
    [HarmonyPatch(typeof(ITab_Pawn_Gear), "DrawThingRow")]
    public static class ITab_Pawn_Gear_DrawThingRow_Transpiler
    {
        // StaticCacheLifecycle: 进程级提示注入方法反射缓存；不持有游戏对象。
        private static readonly MethodInfo TranslateMethod =
            AccessTools.Method(typeof(Translator), nameof(Translator.Translate), new[] { typeof(string) });

        private static readonly MethodInfo TooltipMethod =
            AccessTools.Method(typeof(ITab_Pawn_Gear_DrawThingRow_Transpiler), nameof(DropThingTooltip));

        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (ModLister.GetActiveModWithIdentifier("rimworld.bondagetoys") != null)
            {
                foreach (CodeInstruction inst in instructions)
                {
                    yield return inst;
                }

                yield break;
            }

            List<CodeInstruction> codes = instructions.ToList();
            bool patched = false;

            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldstr &&
                    CodeInstructionExtensions.OperandIs(codes[i], "DropThingLocked") &&
                    i + 1 < codes.Count &&
                    codes[i + 1].Calls(TranslateMethod))
                {
                    // 原版 DrawThingRow 的 thing 参数稳定可用，不能依赖编译器生成的局部变量编号。
                    yield return new CodeInstruction(OpCodes.Ldarg_3);
                    yield return new CodeInstruction(OpCodes.Call, TooltipMethod);
                    i++;
                    patched = true;
                    continue;
                }

                yield return codes[i];
            }

            if (!patched)
            {
                MugirlLog.WarningOnce(
                    "DropThingTooltip.TranspilerNotApplied",
                    "Mugirl.DropThingTooltip.TranspilerNotApplied".Translate().ToString());
            }
        }

        public static TaggedString DropThingTooltip(Thing thing)
        {
            if (thing is Apparel apparel &&
                (apparel.IsAdvancedApparel() || apparel.IsSlaveApparel()))
            {
                return "Mugirl.SlaveApparelCannotBeRemoved".Translate();
            }

            return "DropThingLocked".Translate();
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.Unlock))]
    public static class Pawn_ApparelTracker_Unlock_SlaveApparel_Patch
    {
        public static bool Prefix(Pawn_ApparelTracker __instance, Apparel apparel)
        {
            // Key and crack flows clear the custom lock first; removal callbacks run after WornApparel is updated.
            return !apparel.IsLockedSlaveApparel()
                || __instance?.WornApparel?.Contains(apparel) != true;
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.ExposeData))]
    public static class Pawn_ApparelTracker_ExposeData_SlaveApparel_Patch
    {
        public static void Postfix(Pawn_ApparelTracker __instance)
        {
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                __instance?.pawn.EnsureWornSlaveApparelLocks();
            }
        }
    }

    [HarmonyPatch]
    public static class Pawn_ApparelTracker_TryDropFull_SlaveApparel_Patch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Pawn_ApparelTracker),
                nameof(Pawn_ApparelTracker.TryDrop),
                new[] { typeof(Apparel), typeof(Apparel).MakeByRefType(), typeof(IntVec3), typeof(bool) });
        }

        public static bool Prefix(Apparel ap, ref Apparel resultingAp, ref bool __result)
        {
            if (SlaveApparelDropGuard.ShouldBlockDirectDrop(ap))
            {
                resultingAp = null;
                __result = false;
                SlaveApparelDropGuard.RejectDropMessage();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch]
    public static class Pawn_ApparelTracker_TryDropWithResult_SlaveApparel_Patch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Pawn_ApparelTracker),
                nameof(Pawn_ApparelTracker.TryDrop),
                new[] { typeof(Apparel), typeof(Apparel).MakeByRefType() });
        }

        public static bool Prefix(Apparel ap, ref Apparel resultingAp, ref bool __result)
        {
            if (SlaveApparelDropGuard.ShouldBlockDirectDrop(ap))
            {
                resultingAp = null;
                __result = false;
                SlaveApparelDropGuard.RejectDropMessage();
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch]
    public static class Pawn_ApparelTracker_TryDropSimple_SlaveApparel_Patch
    {
        public static MethodBase TargetMethod()
        {
            return AccessTools.Method(
                typeof(Pawn_ApparelTracker),
                nameof(Pawn_ApparelTracker.TryDrop),
                new[] { typeof(Apparel) });
        }

        public static bool Prefix(Apparel ap, ref bool __result)
        {
            if (SlaveApparelDropGuard.ShouldBlockDirectDrop(ap))
            {
                __result = false;
                SlaveApparelDropGuard.RejectDropMessage();
                return false;
            }

            return true;
        }
    }

    internal static class SlaveApparelDropGuard
    {
        internal static bool ShouldBlockDirectDrop(Apparel apparel)
        {
            return apparel is SlaveApparel slaveApparel
                && slaveApparel.isLocked
                && apparel.Wearer != null
                && !apparel.Wearer.Dead;
        }

        internal static void RejectDropMessage()
        {
            Messages.Message("Mugirl.SlaveApparelCannotBeRemoved".Translate(), MessageTypeDefOf.RejectInput, historical: false);
        }
    }

    [HarmonyPatch(typeof(Pawn_ApparelTracker), nameof(Pawn_ApparelTracker.WouldReplaceLockedApparel))]
    public static class Pawn_ApparelTracker_WouldReplaceLockedApparel_SlaveApparel_Patch
    {
        public static void Postfix(Pawn ___pawn, Apparel newApparel, ref bool __result)
        {
            if (!__result && SlaveApparelWearGuard.WouldReplaceLockedSlaveApparel(___pawn, newApparel))
            {
                __result = true;
            }
        }
    }

    [HarmonyPatch(typeof(FloatMenuOptionProvider_Wear), "GetSingleOptionFor", new[] { typeof(Thing), typeof(FloatMenuContext) })]
    public static class FloatMenuOptionProvider_Wear_SlaveApparel_Patch
    {
        public static void Postfix(Thing clickedThing, FloatMenuContext context, ref FloatMenuOption __result)
        {
            Pawn pawn = context?.FirstSelectedPawn;
            Apparel apparel = clickedThing as Apparel;
            if (!SlaveApparelWearGuard.WouldReplaceLockedSlaveApparel(pawn, apparel))
            {
                return;
            }

            TaggedString label = "Mugirl.FloatMenu.OptionWithReason".Translate(
                "ForceWear".Translate(clickedThing.LabelShort),
                "Mugirl.SlaveApparelCannotBeRemoved".Translate());
            __result = new FloatMenuOption(label, null, MenuOptionPriority.DisabledOption);
        }
    }

    [HarmonyPatch(typeof(JobDriver_Wear), nameof(JobDriver_Wear.TryMakePreToilReservations))]
    public static class JobDriver_Wear_TryMakePreToilReservations_SlaveApparel_Patch
    {
        public static bool Prefix(JobDriver_Wear __instance, ref bool __result)
        {
            if (SlaveApparelWearGuard.WouldReplaceLockedSlaveApparel(__instance.GetActor(), SlaveApparelWearGuard.JobTargetApparel(__instance.job)))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(JobDriver_Wear), "TryUnequipSomething")]
    public static class JobDriver_Wear_TryUnequipSomething_SlaveApparel_Patch
    {
        public static bool Prefix(JobDriver_Wear __instance)
        {
            if (SlaveApparelWearGuard.WouldReplaceLockedSlaveApparel(__instance.GetActor(), SlaveApparelWearGuard.JobTargetApparel(__instance.job)))
            {
                __instance.EndJobWith(JobCondition.Incompletable);
                return false;
            }

            return true;
        }
    }

    internal static class SlaveApparelWearGuard
    {
        internal static Apparel JobTargetApparel(Job job)
        {
            if (job == null)
            {
                return null;
            }

            LocalTargetInfo target = job.GetTarget(TargetIndex.A);
            return target.HasThing ? target.Thing as Apparel : null;
        }

        internal static bool WouldReplaceLockedSlaveApparel(Pawn pawn, Apparel newApparel)
        {
            if (pawn?.apparel?.WornApparel == null || newApparel?.def == null || pawn.RaceProps?.body == null)
            {
                return false;
            }

            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                Apparel worn = wornApparel[i];
                if (worn == null || worn == newApparel)
                {
                    continue;
                }

                if (worn is SlaveApparel slaveApparel
                    && slaveApparel.isLocked
                    && !ApparelUtility.CanWearTogether(newApparel.def, worn.def, pawn.RaceProps.body))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
