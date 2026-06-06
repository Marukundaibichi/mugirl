using HarmonyLib;
using MooGirl;
using RimWorld;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using Verse;

namespace BondageToys
{
    [HarmonyPatch(typeof(ITab_Pawn_Gear), "DrawThingRow")]
    public static class ITab_Pawn_Gear_DrawThingRow_Transpiler
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            if (ModLister.GetActiveModWithIdentifier("rimworld.bondagetoys") != null)
            {
                foreach (var inst in instructions)
                {
                    yield return inst;
                }
                yield break;
            }

            List<CodeInstruction> codes = instructions.ToList();
            for (int i = 0; i < codes.Count; i++)
            {
                if (codes[i].opcode == OpCodes.Ldstr &&
                    CodeInstructionExtensions.OperandIs(codes[i], "DropThingLocked"))
                {
                    yield return new CodeInstruction(OpCodes.Ldloc_S, 10);
                    yield return new CodeInstruction(
                        OpCodes.Call,
                        AccessTools.Method(
                            typeof(ITab_Pawn_Gear_DrawThingRow_Transpiler),
                            nameof(DropThingTooltip)
                        )
                    );
                    i++;
                    continue;
                }

                yield return codes[i];
            }
        }

        public static TaggedString DropThingTooltip(Apparel apparel)
        {
            if (apparel.IsAdvancedApparel() || apparel.IsSlaveApparel())
            {
                return "MooGirl.SlaveApparelCannotBeRemoved".Translate();
            }
            return "DropThingLodger".Translate();
        }
    }
}
