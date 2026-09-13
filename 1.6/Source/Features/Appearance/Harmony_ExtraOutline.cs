using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(PawnRenderTree), nameof(PawnRenderTree.Draw))]
    public static class Harmony_ExtraOutline_Body
    {
        // Consume the final visible requests/matrices prepared by the game and HAR.
        // No extra tree traversal, no second animation evaluation, no changes to requests.
        public static void Prefix(PawnRenderTree __instance, PawnDrawParms parms,
            List<PawnGraphicDrawRequest> ___drawRequests)
        {
            MugirlExtraOutline.DrawBody(__instance.pawn, ___drawRequests, parms);
        }
    }

    [HarmonyPatch(typeof(PawnRenderUtility), nameof(PawnRenderUtility.DrawEquipmentAiming))]
    public static class Harmony_ExtraOutline_Weapon
    {
        public static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo draw = AccessTools.Method(typeof(Graphics), nameof(Graphics.DrawMesh),
                new[] { typeof(Mesh), typeof(Matrix4x4), typeof(Material), typeof(int) });
            MethodInfo replacement = AccessTools.Method(typeof(Harmony_ExtraOutline_Weapon), nameof(DrawWithOutline));
            int matches = 0;
            var codes = new List<CodeInstruction>(instructions);
            foreach (CodeInstruction code in codes) if (code.Calls(draw)) matches++;
            if (matches != 1)
            {
                MugirlLog.WarningOnce("ExtraOutline.WeaponAnchor", "Mugirl.Outline.WeaponPatchUnavailable".Translate().ToString());
                return codes;
            }
            var result = new List<CodeInstruction>(codes.Count + 1);
            foreach (CodeInstruction code in codes)
            {
                if (code.Calls(draw))
                {
                    // The verified 1.6 IL already has the final mesh/matrix/material on
                    // the stack. Reuse recoil, flip, style and other mods' transforms.
                    var equipment = new CodeInstruction(OpCodes.Ldarg_0);
                    equipment.MoveLabelsFrom(code);
                    result.Add(equipment);
                    code.opcode = OpCodes.Call;
                    code.operand = replacement;
                }
                result.Add(code);
            }
            return result;
        }

        public static void DrawWithOutline(Mesh mesh, Matrix4x4 matrix, Material material, int layer, Thing equipment)
        {
            if (MugirlExtraOutline.Enabled)
            {
                Pawn pawn = (equipment?.ParentHolder as Pawn_EquipmentTracker)?.pawn;
                MugirlExtraOutline.DrawWeapon(pawn, mesh, matrix, material);
            }
            Graphics.DrawMesh(mesh, matrix, material, layer);
        }
    }
}
