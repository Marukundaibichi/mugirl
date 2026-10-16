namespace Mugirl.Features.Appearance;

/// <summary>只在当前发型匹配且不是婴儿或新生儿时绘制后发，并按朝向选择南向或侧向贴图。</summary>
public sealed class PawnRenderNodeWorker_BackHair : PawnRenderNodeWorker_FlipWhenCrawling
{
    public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
    {
        return base.CanDrawNow(node, parms)
            && node is PawnRenderNode_BackHair backHair
            && parms.pawn is { } pawn
            && pawn.story?.hairDef == backHair.BackHairDef
            && !pawn.DevelopmentalStage.Baby()
            && !pawn.DevelopmentalStage.Newborn();
    }

    protected override Graphic? GetGraphic(PawnRenderNode node, PawnDrawParms parms)
    {
        return node is PawnRenderNode_BackHair backHair
            ? backHair.GraphicForFacing(parms.pawn, parms.facing)
            : null;
    }
}
