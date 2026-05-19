using HarmonyLib;
using UnityEngine;
using Verse;

namespace MooGirl
{
    [StaticConstructorOnStartup]
    public static class Patch_AlienPawnRenderNode_Swaddle_GraphicFor
    {
        static Patch_AlienPawnRenderNode_Swaddle_GraphicFor()
        {
            // Do not hard-reference HAR types at compile time; patch only if present at runtime.
            var swaddleType = AccessTools.TypeByName("AlienRace.AlienPawnRenderNode_Swaddle");
            if (swaddleType == null) return;

            var target = AccessTools.Method(swaddleType, "GraphicFor", new[] { typeof(Pawn) });
            if (target == null) return;

            var prefix = AccessTools.Method(typeof(Patch_AlienPawnRenderNode_Swaddle_GraphicFor), nameof(Prefix));
            if (prefix == null) return;

            new Harmony("MooGirl.SwaddlePatch").Patch(target, prefix: new HarmonyMethod(prefix));
        }

        static bool Prefix(Pawn pawn, ref Graphic __result)
        {
            if (pawn?.def == MooGirl_DefOf.MooGirl)
            {
                const string path = "MooGirl/Bodies/SwaddledBaby/Swaddled_Child";
                __result = GraphicDatabase.Get<Graphic_Multi>(path, ShaderDatabase.Cutout, Vector2.one, Color.white);
                return false;
            }

            return true;
        }
    }
}
