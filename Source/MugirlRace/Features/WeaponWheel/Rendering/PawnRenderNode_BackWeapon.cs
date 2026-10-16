namespace Mugirl.Features.WeaponWheel;

// 普通背负武器节点：显示轮盘第 1、2 格（displayIndex 0、1）未手持的武器，摆放来自 WeaponWheelBackLayout。
public sealed class PawnRenderNode_BackWeapon : PawnRenderNode_WheelWeapon
{
    public PawnRenderNode_BackWeapon(
        Pawn pawn,
        PawnRenderNodeProperties props,
        PawnRenderTree tree,
        CompWeaponWheel? wheelComp,
        int displayIndex)
        : base(pawn, props, tree, wheelComp, displayIndex)
    {
        wheelComp?.RegisterBackWeaponNode(this);
    }

    internal override ThingWithComps? Weapon => WheelComp?.GetBackWeapon(DisplayIndex);

    internal bool HasAutoloadingSystem => WheelComp?.HasAutoloadingSystem == true;

    internal bool VisibleInStorage => WheelComp?.IsBackWeaponVisible(DisplayIndex) == true;

    public override Mesh? GetMesh(PawnDrawParms parms)
    {
        // 人物网格的 backLift 相当于 5 层；枪械旋转后会穿过身体/装弹架，南向改用平面网格。
        Mesh? mesh = parms.facing == Rot4.South ? FlatSouthMesh(parms) : base.GetMesh(parms);
        WeaponWheelBackLayoutEntry entry = WeaponWheelBackLayout.GetEntry(DisplayIndex, parms.facing);
        // 上下镜像 = 左右镜像 + 旋转 180°（旋转在 Worker.RotationFor 中加上），两者同时开启时只剩旋转。
        return entry.FlipHorizontal != entry.FlipVertical ? MirroredHorizontally(mesh) : mesh;
    }

    // 同一几何、左右翻转 UV 的 Verse 网格：1×1 带 backLift 的人物网格在 GraphicMeshSet 中成对存在
    // （北/东/南为未翻转，西为翻转），平面网格由 MeshPool 成对缓存。其他网格不处理。
    private Mesh? MirroredHorizontally(Mesh? mesh)
    {
        if (mesh is null || meshSet is null)
        {
            return mesh;
        }

        Mesh unflippedLifted = meshSet.MeshAt(Rot4.North);
        Mesh flippedLifted = meshSet.MeshAt(Rot4.West);
        if (ReferenceEquals(mesh, unflippedLifted))
        {
            return flippedLifted;
        }
        if (ReferenceEquals(mesh, flippedLifted))
        {
            return unflippedLifted;
        }

        UVector2 unit = new(1f, 1f);
        Mesh flat = MeshPool.GridPlane(unit);
        Mesh flatFlipped = MeshPool.GridPlaneFlip(unit);
        if (ReferenceEquals(mesh, flat))
        {
            return flatFlipped;
        }
        return ReferenceEquals(mesh, flatFlipped) ? flat : mesh;
    }
}
