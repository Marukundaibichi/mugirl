namespace Mugirl.Features.WeaponWheel;

// 自驱装弹系统凹槽中的武器节点：显示轮盘第 3–6 格（displayIndex 0–3）的武器，仅在穿戴装弹系统时有贴图。
public sealed class PawnRenderNode_AutoloadingWeapon : PawnRenderNode_WheelWeapon
{
    public PawnRenderNode_AutoloadingWeapon(
        Pawn pawn,
        PawnRenderNodeProperties props,
        PawnRenderTree tree,
        CompWeaponWheel? wheelComp,
        int displayIndex)
        : base(pawn, props, tree, wheelComp, displayIndex)
    {
        wheelComp?.RegisterAutoloadingWeaponNode(this);
    }

    internal override ThingWithComps? Weapon => WheelComp?.GetAutoloadingWeapon(DisplayIndex);

    public override Mesh? GetMesh(PawnDrawParms parms)
    {
        // 南向槽枪旋转 90° 后也必须保持等高，不能使用人物网格的 backLift。
        return parms.facing == Rot4.South ? FlatSouthMesh(parms) : base.GetMesh(parms);
    }
}
