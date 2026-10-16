using System.Diagnostics.CodeAnalysis;
using Verse.Sound;

namespace Mugirl.Features.Abilities;

// 开发者模式的“流星扣篮”控制器（XML thingClass，存档按类名保存）：运球 → 盘旋起跳 → 随飞行者飞行 → 下扣 → 触地碎裂。
// 本文件为状态机、tick 与存档；绘制见 DunkController.Presentation.cs，触地结算与放下载荷见 DunkController.Impact.cs。
public partial class DunkController : Thing, IThingHolderTickable, IThingHolder
{
    private DunkMotionState motion = new();

    private const int FinishedLifetimeTicks = 90;
    // 运球触地效果的周期（tick）。
    private const int DribbleBouncePeriodTicks = 15;

    private ThingOwner<Thing> innerContainer;
    private Pawn? carrier;
    private Pawn? victim;
    private Ability? sourceAbility;
    private IntVec3 originalPosition;
    private Rot4 originalRotation = Rot4.North;
    private float mass;
    private IntVec3 impactCell;
    private IntVec3 landingCell;
    private bool resolving;
    private bool pendingDrop;
    private bool bleeds;
    private ThingDef? bloodDef;

    public DunkController()
    {
        innerContainer = new ThingOwner<Thing>(this, oneStackOnly: true);
    }

    public Pawn? Carrier => carrier;
    public bool HasPayload => Payload is not null;
    public bool SequenceStarted => motion.phase != DunkPhase.Dribbling;
    public bool Completed => motion.phase == DunkPhase.Finished;
    public bool ShouldTickContents => motion.phase != DunkPhase.Finished;
    public ThingOwner SearchableContents => innerContainer;

    private Thing? Payload => innerContainer is { Count: > 0 } ? innerContainer[0] : null;

    public static bool TryCreate(Pawn caster, Pawn victim, Ability? ability, [NotNullWhen(true)] out DunkController? prop)
    {
        prop = null;
        if (!DunkUtility.CanDunk(caster, victim, out BodyPartRecord? head, out _)
            || AbilitiesDefOf.Mugirl_DunkProp is not { } propDef || AbilitiesDefOf.Mugirl_DunkHead is not { } headDef)
        {
            return false;
        }

        Map map = caster.Map;
        IntVec3 origin = victim.Position;
        if (ThingMaker.MakeThing(propDef) is not DunkController controller
            || ThingMaker.MakeThing(headDef) is not DunkHead headBall)
        {
            return false;
        }

        headBall.Initialize(victim);
        controller.carrier = caster;
        controller.victim = victim;
        controller.sourceAbility = ability;
        controller.motion.phase = DunkPhase.Dribbling;
        controller.originalPosition = origin;
        controller.originalRotation = Rot4.South;
        controller.mass = ThrowUtility.EffectiveMass(victim);
        controller.bleeds = victim.health.CanBleed;
        controller.bloodDef = controller.bleeds ? victim.RaceProps.BloodDef : null;

        if (!controller.innerContainer.TryAdd(headBall))
        {
            headBall.Destroy();
            return false;
        }

        GenSpawn.Spawn(controller, caster.Position, map, WipeMode.Vanish);
        prop = controller;
        DamageInfo severing = new(DamageDefOf.Cut, 1f, instigator: caster);
        severing.SetHitPart(head);
        victim.health.AddHediff(HediffDefOf.MissingBodyPart, head, severing);
        if (!victim.health.hediffSet.PartIsMissing(head))
        {
            controller.innerContainer.Remove(headBall);
            headBall.Destroy();
            controller.Destroy();
            prop = null;
            return false;
        }

        if (!victim.Dead)
        {
            victim.Kill(severing);
        }

        victim.Drawer.renderer.SetAllGraphicsDirty();
        Vector3 originCenter = origin.ToVector3Shifted().ToNumerics();
        controller.ThrowBloodBurst(originCenter, 12, 0.6f, 1.15f, 1.2f, 2.8f);
        controller.LeaveBlood(origin, 3);
        FleckMaker.ThrowDustPuff(origin.ToVector3Shifted(), map, 1.3f);
        return true;
    }

    public void NotifyRunning()
    {
        // 留足一个拍球周期，避免移动/绘制更新落在不同 tick 时漏掉触地效果。
        motion.runningTicks = 16;
    }

    public bool BeginDunk(IntVec3 destination)
    {
        if (motion.phase != DunkPhase.Dribbling || pendingDrop || Payload is null || carrier is null || Map is null || !destination.InBounds(Map))
        {
            return false;
        }

        if (!TryFindLandingCell(carrier, destination, out IntVec3 safeLandingCell))
        {
            return false;
        }

        impactCell = destination;
        landingCell = safeLandingCell;
        motion.launchCarrierPosition = carrier.DrawPos;
        motion.launchHeadPosition = DribbleDrawPos().ToUnity();
        motion.followsCarrier = true;
        motion.impactAnimating = false;
        motion.impactTracksCarrier = false;
        carrierVisualDrawPos = motion.launchCarrierPosition.ToNumerics();
        motion.launchSpinDegrees = DunkPresentationMath.DribbleSpinDegrees(TickUtility.CurrentGameTickOrFallback(0));
        EnterState(DunkPhase.Spin);
        AbilitiesDefOf.Longjump_Jump.PlayOneShot(new TargetInfo(carrier.Position, Map));
        VerbProperties jumpProps = new()
        {
            soundLanding = AbilitiesDefOf.Longjump_Land
        };
        if (MugirlJumpUtility.DoJump(carrier, landingCell, jumpProps, sourceAbility, impactCell, AbilitiesDefOf.Mugirl_DunkFlyer))
        {
            CacheCarrierVisualDrawPos();
            return true;
        }

        EnterState(DunkPhase.Dribbling);
        motion.followsCarrier = false;
        return false;
    }

    // 切换阶段：阶段计时与显示补间都从 0 开始。
    private void EnterState(DunkPhase next)
    {
        motion.phase = next;
        motion.phaseTicks = 0;
        presentation.Reset();
    }

    protected override void TickInterval(int delta)
    {
        base.TickInterval(delta);
        CacheCarrierVisualDrawPos();
        if (pendingDrop)
        {
            TryDropSafely();
            return;
        }

        if (motion.phase == DunkPhase.Dribbling)
        {
            TickDribbling(delta);
            return;
        }

        motion.phaseTicks += delta;
        switch (motion.phase)
        {
            case DunkPhase.Spin:
                TickSpin();
                break;
            case DunkPhase.Flight:
                TickFlight();
                break;
            case DunkPhase.Slam:
                TickSlam();
                break;
            case DunkPhase.Shattering:
                // 撞击已结算，只完成碎裂演出，不能重复爆炸伤害。
                FinishImpact(explode: false);
                break;
            case DunkPhase.Finished:
                if (motion.phaseTicks >= FinishedLifetimeTicks)
                {
                    Destroy();
                }

                break;
        }
    }

    private void TickDribbling(int delta)
    {
        Map? map = Map;
        if (carrier is null || carrier.Destroyed || carrier.Dead || !carrier.Spawned || carrier.Map != map || map is null)
        {
            TryDropSafely();
            return;
        }

        motion.runningTicks = Math.Max(0, motion.runningTicks - delta);
        int bouncePhase = TickUtility.CurrentGameTickOrFallback(0) % DribbleBouncePeriodTicks;
        if (motion.runningTicks > 0 && bouncePhase < delta)
        {
            Vector3 contact = carrier.DrawPos.ToNumerics() + carrier.Rotation.FacingCell.ToVector3().ToNumerics() * 0.45f;
            contact.Z -= 0.52f;
            UVector3 contactPos = contact.ToUnity();
            FleckMaker.ThrowDustPuff(contactPos, map, 0.72f);
            ThrowBloodBurst(contact, 3, 0.3f, 0.58f, 0.35f, 1f);
            LeaveBlood(contactPos.ToIntVec3(), 1);
            if (Rand.Chance(0.34f))
            {
                FleckMaker.ThrowMicroSparks(contactPos, map);
            }
        }
    }

    private void TickSpin()
    {
        if (motion.phaseTicks % 5 == 0)
        {
            Vector3 dustOrigin = motion.launchHeadPosition.ToNumerics();
            dustOrigin.Z -= 0.35f;
            FleckMaker.ThrowDustPuff((dustOrigin + Gen.RandomHorizontalVector(0.35f).ToNumerics()).ToUnity(), Map, 0.55f);
        }

        if (motion.phaseTicks < DunkPresentationMath.SpinTicks)
        {
            return;
        }

        if (!motion.followsCarrier)
        {
            motion.launchHeadPosition = SpinDrawPosAt(DunkPresentationMath.SpinTicks).ToUnity();
        }

        EnterState(DunkPhase.Flight);
    }

    private void TickFlight()
    {
        if (!motion.followsCarrier)
        {
            // 独立轨迹在第 31 tick 触地；与跟随施术者的轨迹分别计时。
            if (motion.phaseTicks >= DunkPresentationMath.FlightTicksBeforeImpact)
            {
                SoundDefOf.MetalHitImportant?.PlayOneShot(new TargetInfo(impactCell, Map));
                FinishImpact(explode: true);
            }

            return;
        }

        PawnFlyer? activeFlyer = ActiveFlyer;
        // 下坠从飞行弧线顶点开始；头先脱手，以更快的速度砸向地面。
        if (activeFlyer is not null && (activeFlyer is not PawnFlyer_Dunk dunkFlyer || !dunkFlyer.IsDescending))
        {
            return;
        }

        // 专用飞行器从下坠起点触发；其他飞行器结束飞行后开始下扣。
        motion.impactStartHeadPosition = FlightDrawPosAt(presentation.Ticks).ToUnity();
        motion.impactStartCarrierPosition = carrierVisualDrawPos.ToUnity();
        motion.impactStartSpinDegrees = RenderSpinDegrees;
        motion.impactAnimating = true;
        motion.impactTracksCarrier = activeFlyer is PawnFlyer_Dunk;
        EnterState(DunkPhase.Slam);
    }

    // 施术者所在的、仍在地图上的飞行器。
    private PawnFlyer? ActiveFlyer => carrier?.ParentHolder is PawnFlyer flyer && flyer.Spawned && !flyer.Destroyed ? flyer : null;

    private void TickSlam()
    {
        // 动画型下扣等待完整时长；直接撞击型在本 tick 结算。
        if (motion.impactAnimating && motion.phaseTicks < DunkPresentationMath.SlamTicks)
        {
            return;
        }

        SoundDefOf.MetalHitImportant?.PlayOneShot(new TargetInfo(impactCell, Map));
        AbilitiesDefOf.Pawn_Melee_BigBash_HitPawn.PlayOneShot(new TargetInfo(impactCell, Map));
        FinishImpact(explode: true);
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
        Scribe_Deep.Look(ref motion, "motion");
        Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
        Scribe_References.Look(ref carrier, "carrier");
        Scribe_References.Look(ref victim, "victim");
        Scribe_References.Look(ref sourceAbility, "sourceAbility");
        Scribe_Values.Look(ref originalPosition, "originalPosition");
        Scribe_Values.Look(ref originalRotation, "originalRotation", Rot4.North);
        Scribe_Values.Look(ref mass, "mass", 0f);
        Scribe_Values.Look(ref impactCell, "impactCell");
        Scribe_Values.Look(ref landingCell, "landingCell");
        Scribe_Values.Look(ref resolving, "resolving", false);
        Scribe_Values.Look(ref pendingDrop, "pendingDrop", false);
        Scribe_Values.Look(ref bleeds, "bleeds", false);
        Scribe_Defs.Look(ref bloodDef, "bloodDef");
    }
}
