namespace Mugirl.Features.Appearance;

/// <summary>
/// 携带后发路径与目标发型的渲染节点属性；每个 <see cref="CompProperties_BackHair"/> 条目创建一份，供所有 Pawn 共用。
/// </summary>
public sealed class PawnRenderNodeProperties_BackHair : PawnRenderNodeProperties
{
    public HairDef? hairDef;

    public string? southPath;

    public string? sidePath;

    internal static PawnRenderNodeProperties_BackHair Create(CompProperties_BackHair compProps)
    {
        return new PawnRenderNodeProperties_BackHair
        {
            debugLabel = "Mugirl back hair (" + (compProps.hairDef?.defName ?? "LongCascade") + ")",
            nodeClass = typeof(PawnRenderNode_BackHair),
            workerClass = typeof(PawnRenderNodeWorker_BackHair),
            pawnType = RenderNodePawnType.HumanlikeOnly,
            parentTagDef = PawnRenderNodeTagDefOf.Head,
            useGraphic = true,
            baseLayer = -1f,
            drawSize = new UVector2(1f, 1f),
            colorType = AttachmentColorType.Hair,
            skipFlag = RenderSkipFlagDefOf.Hair,
            visibleFacing = compProps.sidePath is not null
                ? new List<Rot4> { Rot4.South, Rot4.East, Rot4.West }
                : new List<Rot4> { Rot4.South },
            hairDef = compProps.hairDef,
            southPath = compProps.southPath,
            sidePath = compProps.sidePath,
        };
    }
}
