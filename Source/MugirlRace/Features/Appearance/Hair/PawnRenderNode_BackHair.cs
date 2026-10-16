namespace Mugirl.Features.Appearance;

/// <summary>后发层节点：南向与侧向各一张 Graphic_Single，按节点缓存。</summary>
public sealed class PawnRenderNode_BackHair : PawnRenderNode
{
    private Graphic? southGraphic;
    private Graphic? sideGraphic;

    public PawnRenderNode_BackHair(Pawn pawn, PawnRenderNodeProperties props, PawnRenderTree tree)
        : base(pawn, props, tree)
    {
    }

    internal HairDef? BackHairDef => BackHairProps.hairDef;

    private PawnRenderNodeProperties_BackHair BackHairProps => (PawnRenderNodeProperties_BackHair)props;

    public override Graphic GraphicFor(Pawn pawn)
    {
        return GetOrCreateSouthGraphic(pawn);
    }

    public override GraphicMeshSet MeshSetFor(Pawn pawn)
    {
        // 头发网格的西向已经镜像；额外 FlipGraphic 会再次翻转，让前后发朝向相反。
        return HumanlikeMeshPoolUtility.GetHumanlikeHairSetForPawn(pawn);
    }

    internal Graphic GraphicForFacing(Pawn pawn, Rot4 facing)
    {
        return facing == Rot4.South
            ? GetOrCreateSouthGraphic(pawn)
            : GetOrCreateSideGraphic(pawn);
    }

    protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
    {
        yield return GetOrCreateSouthGraphic(pawn);
        if (BackHairProps.sidePath is not null)
        {
            yield return GetOrCreateSideGraphic(pawn);
        }
    }

    private Graphic GetOrCreateSouthGraphic(Pawn pawn)
    {
        return southGraphic ??= CreateGraphic(pawn, BackHairProps.southPath);
    }

    private Graphic GetOrCreateSideGraphic(Pawn pawn)
    {
        return sideGraphic ??= CreateGraphic(pawn, BackHairProps.sidePath);
    }

    private Graphic CreateGraphic(Pawn pawn, string? path)
    {
        return GraphicDatabase.Get<Graphic_Single>(
            path,
            ShaderFor(pawn),
            new UVector2(1f, 1f),
            ColorFor(pawn));
    }
}
