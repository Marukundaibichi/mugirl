namespace Mugirl.Features.Appearance;

/// <summary>
/// 发型后发层：跟随 Head 变换，但绘制在头部之后（baseLayer -1），使用头发颜色。
/// 例如 XNN_Hairs8F（LongCascade）与 XNN_Hairs10F（Hairs10，仅南向）。
/// 北向原图已经包含完整后发，因此这里只补南、东、西三个朝向。
/// </summary>
public sealed class CompBackHair : ThingComp
{
    public override List<PawnRenderNode>? CompRenderNodes()
    {
        if (parent is not Pawn pawn
            || pawn.Drawer?.renderer?.renderTree is not { } tree
            || props is not CompProperties_BackHair backHairProps)
        {
            return null;
        }

        return new List<PawnRenderNode>(1)
        {
            new PawnRenderNode_BackHair(pawn, backHairProps.NodeProperties, tree),
        };
    }
}
