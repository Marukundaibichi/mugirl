namespace Mugirl.Features.WeaponWheel;

// 背负与自驱装弹凹槽武器节点的公共部分：贴图取自轮盘槽位中的武器实例，网格固定为 1×1。
// 节点由 CompWeaponWheel.CompRenderNodes 创建，不由 XML 引用。
public abstract class PawnRenderNode_WheelWeapon : PawnRenderNode
{
    private protected PawnRenderNode_WheelWeapon(
        Pawn pawn,
        PawnRenderNodeProperties props,
        PawnRenderTree tree,
        CompWeaponWheel? wheelComp,
        int displayIndex)
        : base(pawn, props, tree)
    {
        WheelComp = wheelComp;
        DisplayIndex = displayIndex;
    }

    internal CompWeaponWheel? WheelComp { get; }

    internal int DisplayIndex { get; }

    // 当前应显示的武器；渲染工作线程调用，只读。
    internal abstract ThingWithComps? Weapon { get; }

    internal bool PreviewActive => WheelComp?.BackLayoutPreviewActive == true;

    // 每次都按当前槽位解析，槽位变化不必等节点重建。
    public override Graphic? PrimaryGraphic => GraphicFor(tree.pawn);

    public override Graphic? GraphicFor(Pawn pawn)
    {
        ThingWithComps? weapon = Weapon;
        Graphic? graphic = weapon?.Graphic;
        if (weapon == null || graphic == null)
        {
            return null;
        }
        // NodeGetMat 用 parms.pawn 而不是武器实例解析 Graphic_Random 等集合贴图，
        // 会选中与手持时不同的文化风格差分；这里先按武器本体解析到具体子贴图。
        if (graphic is Graphic_StackCount stackCountGraphic)
        {
            return stackCountGraphic.SubGraphicForStackCount(1, weapon.def);
        }
        return graphic.ExtractInnerGraphicFor(weapon);
    }

    protected override IEnumerable<Graphic> GraphicsFor(Pawn pawn)
    {
        Graphic? graphic = GraphicFor(pawn);
        if (graphic != null)
        {
            yield return graphic;
        }
    }

    public override GraphicMeshSet MeshSetFor(Pawn pawn)
    {
        return MeshPool.GetMeshSetForSize(1f, 1f);
    }

    // 武器 Graphic 的完整 drawSize，不随人物画布缩放。
    internal UVector3 WeaponScale()
    {
        UVector2? drawSize = Weapon?.Graphic?.drawSize;
        return drawSize is UVector2 size ? new UVector3(size.x, 1f, size.y) : new UVector3(1f, 1f, 1f);
    }

    // 南向使用无 backLift 的平面网格：武器旋转后 backLift 的抬高边会穿过身体或装弹架。
    protected Mesh FlatSouthMesh(PawnDrawParms parms)
    {
        Mesh mesh = MeshPool.GridPlane(new UVector2(1f, 1f));
        return FlipGraphic(parms) ? MeshPool.GridPlaneFlip(mesh) : mesh;
    }
}
