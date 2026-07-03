using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse;

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
}
