using System.Diagnostics.CodeAnalysis;
using HarmonyLib;

namespace Mugirl.Features.LifeStages;

// 原版不为婴儿与新生儿绘制头发（PawnRenderNode_Hair.GraphicFor 返回 null）；雪牛娘婴儿保留头发。
[HarmonyPatch(typeof(PawnRenderNode_Hair), nameof(PawnRenderNode_Hair.GraphicFor))]
[MugirlPatch(PatchModule.LifeStages, PatchRisk.Medium, "Mugirl babies keep the base baby hair rendering suppression.")]
internal static class Harmony_PawnRenderNode_Hair_GraphicFor_BabyHair
{
    private static void Postfix(PawnRenderNode_Hair __instance, Pawn? pawn, ref Graphic? __result)
    {
        if (__result is not null || __instance is null || !ShouldDrawBabyHair(pawn))
        {
            return;
        }

        __result = pawn.story.hairDef.GraphicFor(pawn, __instance.ColorFor(pawn));
    }

    private static bool ShouldDrawBabyHair([NotNullWhen(true)] Pawn? pawn)
    {
        return pawn is not null
            && MugirlIdentity.IsMugirlPawn(pawn)
            && (pawn.DevelopmentalStage.Baby() || pawn.DevelopmentalStage.Newborn())
            && pawn.story?.hairDef is not null
            && !pawn.story.hairDef.noGraphic;
    }
}
