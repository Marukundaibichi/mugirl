namespace Mugirl.Features.WeaponWheel;

// 自驱装弹凹槽武器的 Worker：四个槽口的固定位置，武器横置（东西向各转 ±90°）。
public sealed class PawnRenderNodeWorker_AutoloadingWeapon : PawnRenderNodeWorker
{
    // 512px 穿戴贴图在 Mugirl 的 1.5 倍身体画布上绘制；这些值对应四个槽口中心，
    // 不能按原版 1.0 倍人体画布折算，否则四把武器会一起挤到腰带中央。
    private static readonly float[] FrontX = [-0.54f, -0.35f, 0.35f, 0.54f];
    private static readonly float[] SideX = [-0.71f, -0.51f, -0.71f, -0.51f];
    private static readonly float[] SideZ = [-0.17f, -0.17f, -0.38f, -0.38f];

    public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
    {
        PawnRenderNode_AutoloadingWeapon? grooveNode = node as PawnRenderNode_AutoloadingWeapon;
        if ((parms.Portrait && grooveNode?.PreviewActive != true) || parms.pawn.Dead || parms.pawn.GetPosture().InBed())
        {
            return false;
        }
        return grooveNode?.Weapon != null && base.CanDrawNow(node, parms);
    }

    public override UVector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out UVector3 pivot)
    {
        UVector3 offset = base.OffsetFor(node, parms, out pivot);
        int index = ((PawnRenderNode_AutoloadingWeapon)node).DisplayIndex;
        if (parms.facing == Rot4.South || parms.facing == Rot4.North)
        {
            offset.x += FrontX[index];
            offset.z += parms.facing == Rot4.South ? -0.36f : -0.30f;
            return offset;
        }

        offset.x += parms.facing == Rot4.East ? SideX[index] : -SideX[index];
        offset.z += SideZ[index];
        return offset;
    }

    public override float LayerFor(PawnRenderNode node, PawnDrawParms parms)
    {
        if (parms.facing == Rot4.South)
        {
            // 身体前、装弹架（-3）中、凹槽枪（-4..-7）后。
            // 南向使用无 backLift 的网格，顶点不会再越过架体平面；层值均在原版 -10..100 范围内。
            return -4f - ((PawnRenderNode_AutoloadingWeapon)node).DisplayIndex;
        }
        if (parms.facing == Rot4.North)
        {
            // 原版 utility pack 北向层为 93；武器覆盖凹槽但不被底图吞掉。
            return 94f;
        }
        return 1f;
    }

    public override UQuaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
    {
        return (parms.facing == Rot4.West ? -90f : 90f).ToQuat();
    }

    public override UVector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
    {
        // 凹槽只提供收纳锚点，不改变武器本身的显示尺寸；与普通背负节点一致，沿用 Graphic 的完整 drawSize。
        return ((PawnRenderNode_AutoloadingWeapon)node).WeaponScale();
    }
}
