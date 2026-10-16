using Verse.Sound;

namespace Mugirl.Features.Abilities;

// 举起并投掷的控制器（XML thingClass，存档按类名保存）：举起瞄准 → 飞行（穿透屋顶）→ 落点爆炸并放下载荷。
// 本文件为状态、tick 与存档；绘制见 ThrowController.Presentation.cs，瞄准接口见 ThrowController.Targeting.cs。
public partial class ThrowController : Thing, IThingHolderTickable, IThingHolder
{
    // 枚举成员名按名存档，不能改名。
    private enum ThrowState : byte
    {
        Held,
        Flying,
        Resolving
    }

    private const int HoldTimeoutTicks = 300;

    // StaticCacheLifecycle: 屋顶穿透专用走线缓冲；只在主线程 tick 内使用，每次调用先清空，只保存坐标。
    private static readonly List<IntVec3> tmpRoofLineCells = [];

    private ThingOwner<Thing> innerContainer;
    private Pawn? carrier;
    private Ability? sourceAbility;
    private ThrowState state;
    private IntVec3 originalPosition;
    private Rot4 originalRotation = Rot4.North;
    private int heldTicks;
    private float mass;
    private IntVec3 destination;
    private UVector3 flightStartGround;
    private UVector3 flightStartDraw;
    private int flightTicks;
    private int ticksFlying;
    private IntVec3 lastFlightCell = IntVec3.Invalid;
    private bool resolving;
    // 放置失败后的重试退避，不存档。
    private DropRetryBackoff dropRetry;

    public ThrowController()
    {
        innerContainer = new ThingOwner<Thing>(this, oneStackOnly: true);
    }

    public Pawn? Carrier => carrier;
    public bool IsHeld => state == ThrowState.Held;
    // 投掷进行中：举起瞄准或物体仍在空中；此期间禁止持物者开枪。
    public bool IsThrowInProgress => state == ThrowState.Held || state == ThrowState.Flying;
    public bool ShouldTickContents => state != ThrowState.Resolving;
    public ThingOwner SearchableContents => innerContainer;

    private Thing? HeldThing => innerContainer is { Count: > 0 } ? innerContainer[0] : null;

    private Vector3 FlightStartGround => flightStartGround.ToNumerics();

    public static bool TryCreate(Pawn caster, Thing target, Ability? ability)
    {
        if (!ThrowUtility.CanPickUp(caster, target, out _) || AbilitiesDefOf.Mugirl_ThrowController is not { } controllerDef)
        {
            return false;
        }

        Map map = caster.Map;
        IntVec3 origin = target.Position;
        Rot4 rotation = target.Rotation;
        if (ThingMaker.MakeThing(controllerDef) is not ThrowController controller)
        {
            return false;
        }

        controller.carrier = caster;
        controller.sourceAbility = ability;
        controller.state = ThrowState.Held;
        controller.originalPosition = origin;
        controller.originalRotation = rotation;
        controller.mass = ThrowUtility.EffectiveMass(target);

        target.def.soundPickup?.PlayOneShot(new TargetInfo(target.Position, map));
        GenSpawn.Spawn(controller, caster.Position, map, WipeMode.Vanish);
        target.DeSpawn(DestroyMode.Vanish);
        if (!controller.innerContainer.TryAdd(target))
        {
            GenSpawn.Spawn(target, origin, map, rotation, WipeMode.VanishOrMoveAside);
            controller.Destroy();
            return false;
        }

        controller.BeginAiming();
        return true;
    }

    public override void SpawnSetup(Map map, bool respawningAfterLoad)
    {
        base.SpawnSetup(map, respawningAfterLoad);
        ThrowControllerRegistry.NotifySpawned();
        if (state == ThrowState.Flying)
        {
            flightPresentation.Reset(ticksFlying);
        }
    }

    public override void DeSpawn(DestroyMode mode = DestroyMode.Vanish)
    {
        bool wasSpawned = Spawned;
        base.DeSpawn(mode);
        if (wasSpawned && !Spawned)
        {
            ThrowControllerRegistry.NotifyDespawned();
        }
    }

    private void BeginAiming()
    {
        if (state != ThrowState.Held || carrier is null || !carrier.Spawned)
        {
            SafeDrop(resetCooldown: true);
            return;
        }

        if (!GameUtility.TryBeginTargeting(
            this,
            actionWhenFinished: OnTargetingFinished,
            allowNonSelectedTargetingSource: true,
            requiresAvailableVerb: false))
        {
            SafeDrop(resetCooldown: true);
        }
    }

    private void OnTargetingFinished()
    {
        if (state == ThrowState.Held && !resolving)
        {
            SafeDrop(resetCooldown: true);
        }
    }

    protected override void TickInterval(int delta)
    {
        base.TickInterval(delta);
        if (state == ThrowState.Held)
        {
            TickHeld(delta);
        }
        else if (state == ThrowState.Flying)
        {
            TickFlying(delta);
        }
    }

    private void TickHeld(int delta)
    {
        heldTicks += delta;
        bool targetingThis = GameUtility.IsTargetingSource(this);
        bool otherPlayerOrder = heldTicks > 2
            && carrier?.CurJob is { playerForced: true } curJob
            && curJob.ability != sourceAbility;
        if (carrier is null || !carrier.Spawned || carrier.Map != Map || carrier.Dead
            || carrier.WorkTagIsDisabled(WorkTags.Violent)
            || heldTicks >= HoldTimeoutTicks || !targetingThis || otherPlayerOrder)
        {
            SafeDrop(resetCooldown: true);
        }
    }

    private void TickFlying(int delta)
    {
        IntVec3 previousCell = lastFlightCell.IsValid ? lastFlightCell : flightStartGround.ToIntVec3();
        ticksFlying += delta;
        Vector3 groundPos = ThrowFlightMath.GroundPosition(FlightStartGround, destination.ToVector3Shifted().ToNumerics(), ticksFlying, flightTicks);
        IntVec3 currentCell = groundPos.ToUnity().ToIntVec3();
        PunchThroughRoofs(previousCell, currentCell);
        lastFlightCell = currentCell;

        if (ticksFlying % 4 < delta && currentCell.InBounds(Map))
        {
            FleckMaker.ThrowDustPuff((groundPos + Gen.RandomHorizontalVector(0.18f).ToNumerics()).ToUnity(), Map, 0.55f);
        }

        if (ticksFlying >= flightTicks)
        {
            ResolveImpact();
        }
    }

    private void PunchThroughRoofs(IntVec3 from, IntVec3 to)
    {
        Map? map = Map;
        if (map is null || !from.IsValid || !to.IsValid)
        {
            return;
        }

        // 飞行每 tick 调用：与 GenSight.PointsOnLineOfSight（含起终点）相同的走线写入复用缓冲，不分配枚举器。
        GridLineUtility.LineCellsNonAlloc(from, to, tmpRoofLineCells);
        bool playedSound = false;
        for (int i = 0; i < tmpRoofLineCells.Count; i++)
        {
            IntVec3 cell = tmpRoofLineCells[i];
            if (!cell.InBounds(map) || !cell.Roofed(map))
            {
                continue;
            }

            map.roofGrid.SetRoof(cell, null);
            FleckMaker.ThrowDustPuff((cell.ToVector3Shifted().ToNumerics() + Gen.RandomHorizontalVector(0.35f).ToNumerics()).ToUnity(), map, 2.4f);
            if (!playedSound)
            {
                SoundDefOf.Roof_Collapse.PlayOneShot(new TargetInfo(cell, map));
                playedSound = true;
            }
        }
    }

    private void Launch(IntVec3 targetCell)
    {
        if (!CanHitTarget(targetCell) || carrier is null)
        {
            return;
        }

        float distance = carrier.Position.DistanceTo(targetCell);
        state = ThrowState.Flying;
        destination = targetCell;
        flightStartGround = carrier.DrawPos;
        flightStartDraw = HeldDrawPos().ToUnity();
        flightTicks = ThrowFlightMath.FlightTicks(distance);
        ticksFlying = 0;
        flightPresentation.Reset();
        lastFlightCell = carrier.Position;
        AbilitiesDefOf.Longjump_Jump.PlayOneShot(new TargetInfo(carrier.Position, Map));
    }

    private void ResolveImpact()
    {
        Map? map = Map;
        if (resolving || map is null)
        {
            return;
        }

        resolving = true;
        state = ThrowState.Resolving;
        float distance = flightStartGround.ToIntVec3().DistanceTo(destination);
        int damage = ThrowBalance.ImpactDamage(mass, distance);
        float radius = ThrowBalance.ExplosionRadius(damage);
        DamageDef impactDef = AbilitiesDefOf.Mugirl_ThrownImpact ?? DamageDefOf.Bomb;

        GenExplosion.DoExplosion(
            center: destination,
            map: map,
            radius: radius,
            damType: impactDef,
            instigator: carrier,
            damAmount: damage,
            armorPenetration: damage * 0.012f,
            damageFalloff: true,
            ignoredThings: [this],
            screenShakeFactor: UnityMathf.Clamp(radius / 3f, 0.8f, 2f));

        Vector3 destinationCenter = destination.ToVector3Shifted().ToNumerics();
        for (int i = 0; i < 5; i++)
        {
            FleckMaker.ThrowDustPuff((destinationCenter + Gen.RandomHorizontalVector(radius * 0.45f).ToNumerics()).ToUnity(), map, Rand.Range(1.4f, 2.8f));
        }

        Thing? landed = PayloadPlacement.TryPlace(innerContainer, destination, originalPosition, map, originalRotation, "Throw.PlaceFailed.");
        if (landed is null)
        {
            resolving = false;
            state = ThrowState.Held;
            // 没有安全落点时按退避节奏在后续 Tick 重试归还，避免每 tick 全径向重扫。
            ScheduleDropRetry(TickUtility.CurrentGameTickOrFallback(0));
            return;
        }

        landed.TakeDamage(new DamageInfo(DamageDefOf.Crush, damage, damage * 0.012f, -1f, carrier));
        TryBreakFurnitureApart(landed, damage);
        Destroy();
    }

    private static void TryBreakFurnitureApart(Thing landed, int damage)
    {
        if (landed is not Building || landed.Destroyed || !landed.def.Minifiable || landed.MaxHitPoints <= 0)
        {
            return;
        }

        float damageFraction = (float)damage / landed.MaxHitPoints;
        float chance = UnityMathf.Clamp01((damageFraction - 0.15f) * 0.75f);
        if (Rand.Chance(chance))
        {
            landed.Destroy(DestroyMode.Deconstruct);
        }
    }

    private void SafeDrop(bool resetCooldown)
    {
        if (resolving || Destroyed)
        {
            return;
        }

        // 上次放置失败后的退避窗口内不再重试，避免每 tick 重复全径向落点扫描。
        int now = TickUtility.CurrentGameTickOrFallback(0);
        if (dropRetry.IsWaiting(now))
        {
            return;
        }

        resolving = true;
        state = ThrowState.Resolving;
        GameUtility.TryStopTargeting(this);

        Map? map = Map;
        IntVec3 preferred = carrier is not null && carrier.Spawned && carrier.Map == map ? carrier.Position : originalPosition;
        Thing? dropped = PayloadPlacement.TryPlace(innerContainer, preferred, originalPosition, map, originalRotation, "Throw.PlaceFailed.");
        if (dropped is null)
        {
            resolving = false;
            state = ThrowState.Held;
            ScheduleDropRetry(now);
            return;
        }

        if (resetCooldown)
        {
            sourceAbility?.ResetCooldown();
        }

        Destroy();
    }

    private void ScheduleDropRetry(int now)
    {
        heldTicks = HoldTimeoutTicks;
        dropRetry.Schedule(now);
    }

    public ThingOwner GetDirectlyHeldThings()
    {
        return innerContainer;
    }

    public void GetChildHolders(List<IThingHolder> outChildren)
    {
        ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, innerContainer);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
        Scribe_References.Look(ref carrier, "carrier");
        Scribe_References.Look(ref sourceAbility, "sourceAbility");
        Scribe_Values.Look(ref state, "state", ThrowState.Held);
        Scribe_Values.Look(ref originalPosition, "originalPosition");
        Scribe_Values.Look(ref originalRotation, "originalRotation", Rot4.North);
        Scribe_Values.Look(ref heldTicks, "heldTicks", 0);
        Scribe_Values.Look(ref mass, "mass", 0f);
        Scribe_Values.Look(ref destination, "destination");
        Scribe_Values.Look(ref flightStartGround, "flightStartGround");
        Scribe_Values.Look(ref flightStartDraw, "flightStartDraw");
        Scribe_Values.Look(ref flightTicks, "flightTicks", 0);
        Scribe_Values.Look(ref ticksFlying, "ticksFlying", 0);
        Scribe_Values.Look(ref lastFlightCell, "lastFlightCell");
    }
}
