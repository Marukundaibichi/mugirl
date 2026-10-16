namespace Mugirl.Features.WeaponWheel;

// 普通背负武器的 Worker。位置、角度与图层每帧读 WeaponWheelBackLayout（在 ParallelPreDraw 工作线程上），
// 调试窗口改动后立即生效；只有镜像需要重建绘制请求（由调试窗口触发）。
public sealed class PawnRenderNodeWorker_BackWeapon : PawnRenderNodeWorker
{
    public override bool CanDrawNow(PawnRenderNode node, PawnDrawParms parms)
    {
        PawnRenderNode_BackWeapon? backNode = node as PawnRenderNode_BackWeapon;
        bool preview = backNode?.PreviewActive == true;
        if ((parms.Portrait && !preview) || parms.pawn.Dead || parms.pawn.GetPosture().InBed())
        {
            return false;
        }
        // 设置项：隐藏普通背负武器（仅背上的两把；自驱装弹凹槽武器不受影响），调试窗口预览不受此开关影响。
        if (MugirlMod.Settings?.hideWeaponWheelBackWeapons == true && !preview)
        {
            return false;
        }
        return backNode?.VisibleInStorage == true && base.CanDrawNow(node, parms);
    }

    public override UVector3 OffsetFor(PawnRenderNode node, PawnDrawParms parms, out UVector3 pivot)
    {
        UVector3 offset = base.OffsetFor(node, parms, out pivot);
        WeaponWheelBackLayoutEntry entry = WeaponWheelBackLayout.GetEntry(((PawnRenderNode_BackWeapon)node).DisplayIndex, parms.facing);
        offset.x += entry.OffsetX;
        offset.z += entry.OffsetZ;
        return offset;
    }

    public override float LayerFor(PawnRenderNode node, PawnDrawParms parms)
    {
        PawnRenderNode_BackWeapon backNode = (PawnRenderNode_BackWeapon)node;
        if (parms.facing == Rot4.South && backNode.HasAutoloadingSystem)
        {
            // 穿戴装弹系统时，普通背负枪位于南向装弹架（-3 层）后方。
            return WeaponWheelBackLayout.SouthLayerWithAutoloading;
        }
        return WeaponWheelBackLayout.GetEntry(backNode.DisplayIndex, parms.facing).Layer;
    }

    public override UQuaternion RotationFor(PawnRenderNode node, PawnDrawParms parms)
    {
        WeaponWheelBackLayoutEntry entry = WeaponWheelBackLayout.GetEntry(((PawnRenderNode_BackWeapon)node).DisplayIndex, parms.facing);
        // 上下镜像由左右镜像网格加 180° 旋转实现（见 PawnRenderNode_BackWeapon.GetMesh）。
        float angle = entry.FlipVertical ? entry.Angle + 180f : entry.Angle;
        return angle.ToQuat();
    }

    public override UVector3 ScaleFor(PawnRenderNode node, PawnDrawParms parms)
    {
        return ((PawnRenderNode_BackWeapon)node).WeaponScale();
    }
}
