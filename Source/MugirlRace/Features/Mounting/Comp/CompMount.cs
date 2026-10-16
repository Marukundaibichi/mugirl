using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Mounting;

// 挂在每个雪牛娘身上的骑乘组件：用 ThingOwner 保存骑手，提供骑手与武器的绘制位置。
// 组件保持资产所有权，调度累计值与炮塔状态由独立对象维护。
public sealed partial class CompMount : ThingComp, IThingHolder
{
    private ThingOwner<Thing> mugirlMountRiders;
    private bool mugirlMountFireAtWill;
    private readonly MountTickSchedule mugirlMountSchedule = new();
    private readonly MountTurretState mugirlMountTurret = new();

    internal MountTickSchedule Schedule => mugirlMountSchedule;
    internal MountTurretState Turret => mugirlMountTurret;
    public bool FireAtWill
    {
        get => mugirlMountFireAtWill;
        set => mugirlMountFireAtWill = value;
    }

    // 运行期 verb 缓存：以骑手当前主武器引用为键，武器或骑手变化时自动失效；不写入存档。
    internal ThingWithComps? cachedPrimaryWeapon;
    internal Verb? cachedPrimaryVerb;

    public CompMount()
    {
        MakeContainer();
    }

    IThingHolder IThingHolder.ParentHolder => parent.MapHeld ?? parent.ParentHolder;

    public CompProperties_Mount? Props => props as CompProperties_Mount;

    public Pawn? MooPawn => parent as Pawn;

    public Pawn? MountedPawn
    {
        get
        {
            for (int i = 0; i < mugirlMountRiders.Count; i++)
            {
                if (mugirlMountRiders[i] is Pawn pawn)
                {
                    return pawn;
                }
            }

            return null;
        }
    }

    [MemberNotNullWhen(true, nameof(MountedPawn))]
    public bool HasMountedPawn => MountedPawn is not null;

    public override void Initialize(CompProperties props)
    {
        base.Initialize(props);
        mugirlMountFireAtWill = Props?.turretFireAtWillDefault ?? true;
    }

    [MemberNotNull(nameof(mugirlMountRiders))]
    private void MakeContainer()
    {
        mugirlMountRiders ??= new ThingOwner<Thing>(this, oneStackOnly: true, LookMode.Deep, removeContentsIfDestroyed: false);
    }

    // 骑手绘制位置：载具位置 + 头部偏移 + 按朝向的骑乘偏移，高度按朝向单独指定。
    // DrawPhase.ParallelPreDraw 会在 worker 线程调用，这里只读 Pawn 与 Def 数据。
    internal UVector3 RiderDrawPosAt(UVector3 carrierDrawPos)
    {
        Pawn? carrier = MooPawn;
        if (carrier is null)
        {
            return default;
        }

        Rot4 rotation = carrier.Rotation;
        UVector3 headOffset = default;
        if (carrier.Drawer?.renderer is { } renderer && carrier.story?.bodyType is not null)
        {
            headOffset = renderer.BaseHeadOffsetAt(rotation);
        }

        CompProperties_Mount? mountProps = Props;
        float? altitudeOffset = mountProps is null ? null : MountDrawOffsets.RiderAltitudeOffset(mountProps, rotation);
        return MountDrawOffsets.RiderPosition(
            carrierDrawPos.ToNumerics(),
            headOffset.ToNumerics(),
            MountDrawOffsets.RotationOffset(mountProps, rotation),
            altitudeOffset).ToUnity();
    }

    internal UVector3 WeaponDrawPosAt(UVector3 carrierDrawPos)
    {
        UVector3 riderPos = RiderDrawPosAt(carrierDrawPos);
        Pawn? carrier = MooPawn;
        if (carrier is null)
        {
            return riderPos;
        }

        return MountDrawOffsets.WeaponPosition(riderPos.ToNumerics(), carrier.Rotation).ToUnity();
    }
}
