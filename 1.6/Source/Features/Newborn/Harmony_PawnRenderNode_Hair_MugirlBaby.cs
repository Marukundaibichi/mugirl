using HarmonyLib;
using Verse;

namespace Mugirl
{
    [HarmonyPatch(typeof(PawnRenderNode_Hair), nameof(PawnRenderNode_Hair.GraphicFor))]
    internal static class Harmony_PawnRenderNode_Hair_MugirlBaby
    {
        private static void Postfix(PawnRenderNode_Hair __instance, Pawn pawn, ref Graphic __result)
        {
            if (__result != null || __instance == null || !ShouldDrawBabyHair(pawn))
            {
                return;
            }

            __result = pawn.story.hairDef.GraphicFor(pawn, __instance.ColorFor(pawn));
        }

        private static bool ShouldDrawBabyHair(Pawn pawn)
        {
            return pawn != null
                && MugirlIdentity.IsMugirlPawn(pawn)
                && (pawn.DevelopmentalStage.Baby() || pawn.DevelopmentalStage.Newborn())
                && pawn.story?.hairDef != null
                && !pawn.story.hairDef.noGraphic;
        }
    }
}
