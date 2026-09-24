using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Mugirl
{
    public class Thing_MugirlDunkProp : Thing, IThingHolderTickable, IThingHolder
    {
        private enum DunkState : byte
        {
            Dribbling,
            Spin,
            Flight,
            Slam,
            Finished,
            Bounce // 兼容短暂弹跳版本保存的状态；读档后下一 tick 立即碎裂。
        }

        private const int SpinTicks = 18;
        private const int FlightTicksBeforeImpact = 31;
        private const int SlamTicks = 10;
        private const int FinishedLifetimeTicks = 90;
        private const float OrbitDegreesPerTick = 36f;
        private const float HeadSpinDegreesPerTick = 46f;
        // 放置失败后的重试退避：首退 30 tick，逐次翻倍，上限 120 tick。
        private const int FirstDropRetryDelayTicks = 30;
        private const int MaxDropRetryDelayTicks = 120;

        private ThingOwner<Thing> innerContainer;
        private Pawn carrier;
        private Pawn victim;
        private Ability sourceAbility;
        private DunkState state;
        private IntVec3 originalPosition;
        private Rot4 originalRotation = Rot4.North;
        private float mass;
        private IntVec3 impactCell;
        private IntVec3 landingCell;
        private Vector3 launchDrawPos;
        private Vector3 launchCarrierDrawPos;
        private Vector3 slamStartDrawPos;
        private Vector3 slamStartCarrierDrawPos;
        private bool followsCarrier;
        private bool slamAnimating;
        private bool slamTracksCarrier;
        private int stateTicks;
        private int runningTicks;
        private bool resolving;
        private bool pendingDrop;
        // 放置失败后的重试退避（与 Thing_MugirlThrownObject 相同节奏），
        // 避免 pendingDrop 状态每 tick 重复全径向落点扫描。
        private int dropRetryDelayTicks;
        private int dropRetryNotBeforeTick;
        private bool bleeds;
        private ThingDef bloodDef;
        private float launchSpinDegrees;
        private float slamStartSpinDegrees;
        // 仅供绘制补间；不存档，也不参与撞击或破碎判定。
        private float visualTicks;
        private int visualFrame = -1;
        // PawnFlyer.DrawPos 会重算并写入自身位置，只在主线程采样；并行绘制只读缓存。
        private Vector3 carrierVisualDrawPos;

        private Thing Payload => innerContainer != null && innerContainer.Count > 0 ? innerContainer[0] : null;

        public Pawn Carrier => carrier;
        public bool HasPayload => Payload != null;
        public bool SequenceStarted => state != DunkState.Dribbling;
        public bool Completed => state == DunkState.Finished;
        internal float RenderTick => visualTicks;
        internal float RenderSpinDegrees
        {
            get
            {
                if (state == DunkState.Dribbling)
                {
                    return MugirlTickUtility.CurrentGameTickOrFallback(0) * 24f;
                }

                if (state == DunkState.Slam && slamAnimating)
                {
                    return slamStartSpinDegrees + visualTicks * HeadSpinDegreesPerTick;
                }

                float spinTicks = state == DunkState.Spin ? visualTicks
                    : state == DunkState.Flight ? SpinTicks + visualTicks
                    : SpinTicks + FlightTicksBeforeImpact;
                return launchSpinDegrees + spinTicks * HeadSpinDegreesPerTick;
            }
        }
        public bool ShouldTickContents => state != DunkState.Finished;
        public ThingOwner SearchableContents => innerContainer;
        private bool HasAnimatedPresentation => state == DunkState.Spin || state == DunkState.Flight
            || (state == DunkState.Slam && slamAnimating);

        public override Vector3 DrawPos
        {
            get
            {
                switch (state)
                {
                    case DunkState.Spin:
                        return SpinDrawPosAt(visualTicks);
                    case DunkState.Flight:
                        return FlightDrawPosAt(visualTicks);
                    case DunkState.Bounce:
                        return SlamDrawPos;
                    case DunkState.Slam:
                        return slamAnimating ? SlamDrawPosAt(visualTicks) : SlamDrawPos;
                    default:
                        return DribbleDrawPos;
                }
            }
        }

        private Vector3 DribbleDrawPos
        {
            get
            {
                Vector3 center = carrier != null && carrier.Spawned ? carrier.DrawPos : originalPosition.ToVector3Shifted();
                Vector3 facing = carrier != null ? carrier.Rotation.FacingCell.ToVector3() : Vector3.forward;
                float bounce = Mathf.Abs(Mathf.Sin(MugirlTickUtility.CurrentGameTickOrFallback(0) * 0.21f));
                center += facing * 0.28f;
                center.x += Mathf.Sin(MugirlTickUtility.CurrentGameTickOrFallback(0) * 0.10f) * 0.10f;
                center.z += -0.52f + bounce * 0.48f;
                center.y = AltitudeLayer.Pawn.AltitudeFor() + 0.08f + bounce * 0.01f;
                return center;
            }
        }

        private Vector3 SpinDrawPosAt(float renderTick)
        {
            float lift = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(renderTick / 5f));
            float radians = renderTick * OrbitDegreesPerTick * Mathf.Deg2Rad;
            Vector3 center = launchDrawPos;
            center.x += Mathf.Cos(radians) * 0.55f * lift;
            center.z += (1.05f + Mathf.Sin(radians) * 0.30f) * lift;
            center.y = Mathf.Lerp(launchDrawPos.y, AltitudeLayer.MoteOverhead.AltitudeFor() + 0.06f, lift);
            if (followsCarrier)
            {
                center += CarrierVisualDrawPos - launchCarrierDrawPos;
            }
            return center;
        }

        private Vector3 FlightDrawPosAt(float renderTick)
        {
            if (followsCarrier)
            {
                // 篮球仍在施术者手边。飞行者按自身轨迹移动，头只在手边作小幅逐帧摆动。
                Vector3 held = SpinDrawPosAt(SpinTicks);
                float radians = renderTick * HeadSpinDegreesPerTick * Mathf.Deg2Rad;
                held.x += Mathf.Sin(radians) * 0.04f;
                held.z += (1f - Mathf.Cos(radians)) * 0.025f;
                return held;
            }

            float t = Mathf.Clamp01(renderTick / FlightTicksBeforeImpact);
            Vector3 end = impactCell.ToVector3Shifted();
            end.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.02f;
            Vector3 pos = Vector3.Lerp(launchDrawPos, end, t);
            float distance = launchDrawPos.ToIntVec3().DistanceTo(impactCell);
            float apexHeight = Mathf.Clamp(5.6f + distance * 0.18f, 5.8f, 8.5f);
            float height = Mathf.Sin(t * Mathf.PI) * apexHeight;
            pos.z += height * 0.62f;
            pos.y = Mathf.Lerp(launchDrawPos.y, end.y, t) + height * 0.075f;
            return pos;
        }

        private Vector3 CarrierVisualDrawPos => carrierVisualDrawPos;

        private void CacheCarrierVisualDrawPos()
        {
            if (!followsCarrier || (state != DunkState.Spin && state != DunkState.Flight
                && (state != DunkState.Slam || !slamTracksCarrier)))
            {
                return;
            }

            PawnFlyer flyer = carrier?.ParentHolder as PawnFlyer;
            if (flyer != null && !flyer.Destroyed && flyer.Spawned)
            {
                carrierVisualDrawPos = flyer.DrawPos;
            }
            else if (carrier != null && carrier.Spawned && carrier.Map == Map)
            {
                carrierVisualDrawPos = carrier.DrawPos;
            }
            else
            {
                carrierVisualDrawPos = launchCarrierDrawPos;
            }
        }

        private Vector3 SlamDrawPos
        {
            get
            {
                Vector3 pos = impactCell.ToVector3Shifted();
                pos.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.02f;
                return pos;
            }
        }

        private Vector3 SlamDrawPosAt(float renderTick)
        {
            float t = Mathf.Clamp01(renderTick / SlamTicks);
            float acceleratedDescent = 0.35f * t + 0.65f * t * t;
            Vector3 movingStart = slamStartDrawPos;
            if (slamTracksCarrier)
            {
                // 开始下扣时仍继承飞行者的速度，随后逐渐脱手并追向篮筐。
                movingStart += CarrierVisualDrawPos - slamStartCarrierDrawPos;
            }
            return Vector3.Lerp(movingStart, SlamDrawPos, acceleratedDescent);
        }

        public Thing_MugirlDunkProp()
        {
            innerContainer = new ThingOwner<Thing>(this, oneStackOnly: true);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (HasAnimatedPresentation)
            {
                visualTicks = stateTicks;
                visualFrame = -1;
            }
        }

        internal void UpdateDunkPresentation(float frameSeconds, float tickRateMultiplier, bool paused)
        {
            if (!HasAnimatedPresentation || paused)
            {
                return;
            }

            float frameTicks = Mathf.Max(0f, frameSeconds) * 60f * Mathf.Max(0f, tickRateMultiplier);
            float presentationCeiling = state == DunkState.Slam && slamTracksCarrier
                ? Mathf.Min(stateTicks + 1f, SlamTicks - 0.2f) : stateTicks;
            // 下扣显示可领先最多一 tick 以跨过状态切换，但头不能提前画到触地点。
            visualTicks = Mathf.Clamp(visualTicks + frameTicks, 0f, presentationCeiling);
        }

        private void SampleDunkPresentation()
        {
            int frame = Time.frameCount;
            if (visualFrame == frame)
            {
                return;
            }

            CacheCarrierVisualDrawPos();
            if ((visualFrame < 0 && stateTicks > 1)
                || (visualFrame >= 0 && frame > visualFrame + 1))
            {
                visualTicks = Mathf.Max(visualTicks, stateTicks - 1f);
            }
            visualFrame = frame;
            bool running = MugirlTickUtility.TryGetPresentationTickRate(out float tickRateMultiplier);
            UpdateDunkPresentation(Time.unscaledDeltaTime, tickRateMultiplier, !running);
        }

        public static bool TryCreate(Pawn caster, Pawn victim, Ability ability, out Thing_MugirlDunkProp prop)
        {
            prop = null;
            BodyPartRecord head;
            string reason;
            if (!MugirlDunkUtility.CanDunk(caster, victim, out head, out reason)
                || Mugirl_DefOf.Mugirl_DunkProp == null || Mugirl_DefOf.Mugirl_DunkHead == null)
            {
                return false;
            }

            Map map = caster.Map;
            IntVec3 origin = victim.Position;
            Thing_MugirlDunkProp controller = ThingMaker.MakeThing(Mugirl_DefOf.Mugirl_DunkProp) as Thing_MugirlDunkProp;
            Thing_MugirlDunkHead headBall = ThingMaker.MakeThing(Mugirl_DefOf.Mugirl_DunkHead) as Thing_MugirlDunkHead;
            if (controller == null || headBall == null)
            {
                return false;
            }

            headBall.Initialize(victim);
            controller.carrier = caster;
            controller.victim = victim;
            controller.sourceAbility = ability;
            controller.state = DunkState.Dribbling;
            controller.originalPosition = origin;
            controller.originalRotation = Rot4.South;
            controller.mass = MugirlThrowUtility.EffectiveMass(victim);
            controller.bleeds = victim.health.CanBleed;
            controller.bloodDef = controller.bleeds ? victim.RaceProps.BloodDef : null;

            if (!controller.innerContainer.TryAdd(headBall))
            {
                headBall.Destroy();
                return false;
            }

            GenSpawn.Spawn(controller, caster.Position, map, WipeMode.Vanish);
            prop = controller;
            DamageInfo severing = new DamageInfo(DamageDefOf.Cut, 1f, instigator: caster);
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
            controller.ThrowBloodBurst(origin.ToVector3Shifted(), 12, 0.6f, 1.15f, 1.2f, 2.8f);
            controller.LeaveBlood(origin, 3);
            FleckMaker.ThrowDustPuff(origin.ToVector3Shifted(), map, 1.3f);
            return true;
        }

        public void NotifyRunning()
        {
            // 留足一个拍球周期，避免移动/绘制更新落在不同 tick 时漏掉触地效果。
            runningTicks = 16;
        }

        public bool BeginDunk(IntVec3 destination)
        {
            if (state != DunkState.Dribbling || pendingDrop || Payload == null || Map == null || !destination.InBounds(Map))
            {
                return false;
            }

            if (!TryFindLandingCell(carrier, destination, out IntVec3 safeLandingCell))
            {
                return false;
            }

            impactCell = destination;
            landingCell = safeLandingCell;
            launchCarrierDrawPos = carrier.DrawPos;
            launchDrawPos = DribbleDrawPos;
            followsCarrier = true;
            slamAnimating = false;
            slamTracksCarrier = false;
            carrierVisualDrawPos = launchCarrierDrawPos;
            launchSpinDegrees = (MugirlTickUtility.CurrentGameTickOrFallback(0) * 24f) % 360f;
            state = DunkState.Spin;
            stateTicks = 0;
            visualTicks = 0f;
            visualFrame = -1;
            DefDatabase<SoundDef>.GetNamedSilentFail("Longjump_Jump")?.PlayOneShot(new TargetInfo(carrier.Position, Map));
            VerbProperties jumpProps = new VerbProperties
            {
                soundLanding = DefDatabase<SoundDef>.GetNamedSilentFail("Longjump_Land")
            };
            if (JobDriver_CastCharge.DoJump(carrier, landingCell, jumpProps, sourceAbility, impactCell, Mugirl_DefOf.Mugirl_DunkFlyer))
            {
                CacheCarrierVisualDrawPos();
                return true;
            }

            state = DunkState.Dribbling;
            stateTicks = 0;
            visualTicks = 0f;
            visualFrame = -1;
            followsCarrier = false;
            return false;
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

            if (state == DunkState.Dribbling)
            {
                TickDribbling(delta);
                return;
            }

            stateTicks += delta;
            switch (state)
            {
                case DunkState.Spin:
                    TickSpin();
                    break;
                case DunkState.Flight:
                    TickFlight();
                    break;
                case DunkState.Slam:
                    TickSlam();
                    break;
                case DunkState.Bounce:
                    // 旧存档已经完成初次撞击；只补碎头演出，避免再次造成爆炸伤害。
                    FinishImpact(explode: false);
                    break;
                case DunkState.Finished:
                    if (stateTicks >= FinishedLifetimeTicks)
                    {
                        Destroy();
                    }
                    break;
            }
        }

        private void TickDribbling(int delta)
        {
            if (carrier == null || carrier.Destroyed || carrier.Dead || !carrier.Spawned || carrier.Map != Map)
            {
                TryDropSafely();
                return;
            }

            runningTicks = Mathf.Max(0, runningTicks - delta);
            int bouncePhase = MugirlTickUtility.CurrentGameTickOrFallback(0) % 15;
            if (runningTicks > 0 && bouncePhase < delta)
            {
                Vector3 contact = carrier.DrawPos + carrier.Rotation.FacingCell.ToVector3() * 0.45f;
                contact.z -= 0.52f;
                FleckMaker.ThrowDustPuff(contact, Map, 0.72f);
                ThrowBloodBurst(contact, 3, 0.3f, 0.58f, 0.35f, 1f);
                LeaveBlood(contact.ToIntVec3(), 1);
                if (Rand.Chance(0.34f))
                {
                    FleckMaker.ThrowMicroSparks(contact, Map);
                }
            }
        }

        private void TickSpin()
        {
            if (stateTicks % 5 == 0)
            {
                Vector3 dustOrigin = launchDrawPos;
                dustOrigin.z -= 0.35f;
                FleckMaker.ThrowDustPuff(dustOrigin + Gen.RandomHorizontalVector(0.35f), Map, 0.55f);
            }

            if (stateTicks < SpinTicks)
            {
                return;
            }

            if (!followsCarrier)
            {
                launchDrawPos = SpinDrawPosAt(SpinTicks);
            }
            state = DunkState.Flight;
            stateTicks = 0;
            visualTicks = 0f;
            visualFrame = -1;
        }

        private void TickFlight()
        {
            if (!followsCarrier)
            {
                // 旧存档的头沿独立抛物线飞行；维持原来的 31 tick 触地即碎时序。
                if (stateTicks >= FlightTicksBeforeImpact)
                {
                    SoundDefOf.MetalHitImportant?.PlayOneShot(new TargetInfo(impactCell, Map));
                    ResolveSmash();
                }
                return;
            }

            PawnFlyer activeFlyer = carrier?.ParentHolder as PawnFlyer;
            if (activeFlyer != null && activeFlyer.Spawned && !activeFlyer.Destroyed)
            {
                // 下坠从飞行弧线顶点开始；头先脱手，以更快的速度砸向地面。
                if (!(activeFlyer is PawnFlyer_MugirlDunk dunkFlyer) || !dunkFlyer.IsDescending)
                {
                    return;
                }
            }

            // 新飞行器从下坠起点触发；旧档中的普通飞行器落地后兜底。
            slamStartDrawPos = FlightDrawPosAt(visualTicks);
            slamStartCarrierDrawPos = CarrierVisualDrawPos;
            slamStartSpinDegrees = RenderSpinDegrees;
            slamAnimating = true;
            slamTracksCarrier = activeFlyer is PawnFlyer_MugirlDunk && activeFlyer.Spawned && !activeFlyer.Destroyed;
            state = DunkState.Slam;
            stateTicks = 0;
            visualTicks = 0f;
            visualFrame = -1;
        }

        private void TickSlam()
        {
            // 旧版保存在 Slam 的状态没有起点数据，按原行为在下一 tick 直接结算。
            if (slamAnimating && stateTicks < SlamTicks)
            {
                return;
            }

            SoundDefOf.MetalHitImportant?.PlayOneShot(new TargetInfo(impactCell, Map));
            Mugirl_DefOf.Pawn_Melee_BigBash_HitPawn?.PlayOneShot(new TargetInfo(impactCell, Map));
            ResolveSmash();
        }

        private void ResolveSmash()
        {
            FinishImpact(explode: true);
        }

        private void FinishImpact(bool explode)
        {
            if (resolving || Map == null)
            {
                return;
            }

            resolving = true;
            Map map = Map;
            int damage = Mathf.RoundToInt(MugirlThrowUtility.ImpactDamage(mass, 18f) * 1.7f);
            Thing payload = Payload;
            if (payload is Thing_MugirlDunkHead)
            {
                // 触地当刻毁掉容器内的头，不会在地面多绘制一帧完整的头。
                payload.Destroy(DestroyMode.Vanish);
            }
            else
            {
                Thing placed = TryPlacePayload(impactCell, map);
                if (placed == null && HasPayload)
                {
                    resolving = false;
                    TryDropSafely();
                    return;
                }
                ShatterPayload(placed, damage);
            }

            Vector3 smashPos = impactCell.ToVector3Shifted();
            ThrowBloodBurst(smashPos, 36, 0.5f, 1.35f, 1.1f, 3.5f);
            MugirlPunisherDeathEffectUtility.Play(smashPos, map, 1.35f);

            if (explode)
            {
                float radius = Mathf.Clamp(MugirlThrowUtility.ExplosionRadius(damage) * 1.12f, 2.8f, 7.2f);
                DamageDef damageDef = Mugirl_DefOf.Mugirl_ThrownImpact ?? DamageDefOf.Bomb;
                List<Thing> ignored = new List<Thing> { this };
                if (carrier != null)
                {
                    ignored.Add(carrier);
                }
                PawnFlyer activeFlyer = carrier?.ParentHolder as PawnFlyer;
                if (activeFlyer != null && !activeFlyer.Destroyed)
                {
                    ignored.Add(activeFlyer);
                }
                if (victim?.Corpse != null && !victim.Corpse.Destroyed)
                {
                    ignored.Add(victim.Corpse);
                }
                GenExplosion.DoExplosion(
                    center: impactCell,
                    map: map,
                    radius: radius,
                    damType: damageDef,
                    instigator: carrier,
                    damAmount: damage,
                    armorPenetration: damage * 0.015f,
                    damageFalloff: true,
                    ignoredThings: ignored,
                    screenShakeFactor: Mathf.Clamp(radius / 2.2f, 1.3f, 3f));
            }

            LeaveBlood(impactCell, 5);
            state = DunkState.Finished;
            slamAnimating = false;
            slamTracksCarrier = false;
            stateTicks = 0;
            visualTicks = 0f;
            visualFrame = -1;
            resolving = false;
        }

        private void ThrowBloodBurst(Vector3 center, int count, float minScale, float maxScale,
            float minSpeed, float maxSpeed)
        {
            if (!bleeds || bloodDef == null || Map == null || Mugirl_DefOf.Mugirl_DunkBloodSplash == null)
            {
                return;
            }

            Color color = bloodDef.graphicData?.color ?? Color.red;
            color.a = 0.9f;
            for (int i = 0; i < count; i++)
            {
                Vector3 sprayPoint = center + Gen.RandomHorizontalVector(0.18f);
                FleckCreationData fleck = FleckMaker.GetDataStatic(
                    sprayPoint, Map, Mugirl_DefOf.Mugirl_DunkBloodSplash, Rand.Range(minScale, maxScale));
                fleck.velocityAngle = Rand.Range(0f, 360f);
                fleck.velocitySpeed = Rand.Range(minSpeed, maxSpeed);
                fleck.rotationRate = Rand.Range(-180f, 180f);
                fleck.instanceColor = color;
                Map.flecks.CreateFleck(fleck);
            }
        }

        private void LeaveBlood(IntVec3 cell, int count)
        {
            if (bleeds && bloodDef != null && Map != null && cell.InBounds(Map))
            {
                FilthMaker.TryMakeFilth(cell, Map, bloodDef, victim?.LabelShort, count);
            }
        }

        private void ShatterPayload(Thing payload, int damage)
        {
            if (payload == null || payload.Destroyed)
            {
                return;
            }

            Pawn victim = payload as Pawn;
            if (victim != null)
            {
                if (!victim.Dead)
                {
                    DamageInfo fatalImpact = new DamageInfo(DamageDefOf.Crush, Mathf.Max(damage * 6f, 9999f), 1f, -1f, carrier);
                    victim.TakeDamage(fatalImpact);
                    if (!victim.Dead)
                    {
                        victim.Kill(fatalImpact);
                    }
                }

                Corpse corpse = victim.Corpse;
                if (corpse != null && !corpse.Destroyed)
                {
                    corpse.TakeDamage(new DamageInfo(DamageDefOf.Bomb, Mathf.Max(damage * 4f, corpse.MaxHitPoints + 20f), 1f, -1f, carrier));
                    if (!corpse.Destroyed)
                    {
                        corpse.Destroy(DestroyMode.KillFinalize);
                    }
                }
                return;
            }

            payload.TakeDamage(new DamageInfo(DamageDefOf.Crush, Mathf.Max(damage * 6f, payload.MaxHitPoints + 20f), 1f, -1f, carrier));
            if (!payload.Destroyed)
            {
                payload.Destroy(DestroyMode.KillFinalize);
            }
        }

        private Thing TryPlacePayload(IntVec3 preferred, Map map)
        {
            Thing thing = Payload;
            if (thing == null || map == null)
            {
                return null;
            }

            IntVec3 cell;
            if (!MugirlThrowUtility.TryFindPlacementCell(thing, preferred, map, originalRotation, out cell)
                && !MugirlThrowUtility.TryFindPlacementCell(thing, originalPosition, map, originalRotation, out cell))
            {
                return null;
            }

            innerContainer.Remove(thing);
            try
            {
                return GenSpawn.Spawn(thing, cell, map, originalRotation, WipeMode.VanishOrMoveAside);
            }
            catch (Exception ex)
            {
                innerContainer.TryAdd(thing);
                MugirlLog.WarningOnce("Dunk.PlaceFailed." + thing.thingIDNumber, "Mugirl.Throw.PlaceFailed".Translate(thing.LabelCap, ex.Message).ToString());
                return null;
            }
        }

        public void TryDropSafely()
        {
            if (Destroyed || resolving)
            {
                return;
            }

            // 上次放置失败后的退避窗口内不再重试。
            int now = MugirlTickUtility.CurrentGameTickOrFallback(0);
            if (pendingDrop && dropRetryNotBeforeTick > now)
            {
                return;
            }

            pendingDrop = true;
            resolving = true;
            Map map = Map;
            IntVec3 preferred = carrier != null && carrier.Spawned && carrier.Map == map ? carrier.Position : originalPosition;
            Thing dropped = TryPlacePayload(preferred, map);
            if (dropped != null)
            {
                sourceAbility?.ResetCooldown();
                Destroy();
            }
            else
            {
                resolving = false;
                dropRetryDelayTicks = dropRetryDelayTicks <= 0
                    ? FirstDropRetryDelayTicks
                    : Mathf.Min(MaxDropRetryDelayTicks, dropRetryDelayTicks * 2);
                dropRetryNotBeforeTick = now + dropRetryDelayTicks;
            }
        }

        internal static bool TryFindLandingCell(Pawn carrier, IntVec3 center, out IntVec3 landingCell)
        {
            landingCell = IntVec3.Invalid;
            Map map = carrier?.Map;
            if (map == null || !center.IsValid || !center.InBounds(map))
            {
                return false;
            }

            // 飞行者朝南绘制，落在篮筐北侧两格，头才会砸在她前方和画面下方。
            IntVec3 cell = center + new IntVec3(0, 0, 2);
            if (cell.InBounds(map) && JumpUtility.ValidJumpTarget(carrier, map, cell))
            {
                landingCell = cell;
                return true;
            }
            cell = center + new IntVec3(-1, 0, 2);
            if (cell.InBounds(map) && JumpUtility.ValidJumpTarget(carrier, map, cell))
            {
                landingCell = cell;
                return true;
            }
            cell = center + new IntVec3(1, 0, 2);
            if (cell.InBounds(map) && JumpUtility.ValidJumpTarget(carrier, map, cell))
            {
                landingCell = cell;
                return true;
            }
            return false;
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
            Scribe_References.Look(ref victim, "victim");
            Scribe_References.Look(ref sourceAbility, "sourceAbility");
            Scribe_Values.Look(ref state, "state", DunkState.Dribbling);
            Scribe_Values.Look(ref originalPosition, "originalPosition");
            Scribe_Values.Look(ref originalRotation, "originalRotation", Rot4.North);
            Scribe_Values.Look(ref mass, "mass", 0f);
            Scribe_Values.Look(ref impactCell, "impactCell");
            Scribe_Values.Look(ref landingCell, "landingCell");
            Scribe_Values.Look(ref launchDrawPos, "launchDrawPos");
            Scribe_Values.Look(ref launchCarrierDrawPos, "launchCarrierDrawPos");
            Scribe_Values.Look(ref slamStartDrawPos, "slamStartDrawPos");
            Scribe_Values.Look(ref slamStartCarrierDrawPos, "slamStartCarrierDrawPos");
            Scribe_Values.Look(ref followsCarrier, "followsCarrier", false);
            Scribe_Values.Look(ref slamAnimating, "slamAnimating", false);
            Scribe_Values.Look(ref slamTracksCarrier, "slamTracksCarrier", false);
            Scribe_Values.Look(ref stateTicks, "stateTicks", 0);
            Scribe_Values.Look(ref runningTicks, "runningTicks", 0);
            Scribe_Values.Look(ref resolving, "resolving", false);
            Scribe_Values.Look(ref pendingDrop, "pendingDrop", false);
            Scribe_Values.Look(ref bleeds, "bleeds", false);
            Scribe_Defs.Look(ref bloodDef, "bloodDef");
            Scribe_Values.Look(ref launchSpinDegrees, "launchSpinDegrees", 0f);
            Scribe_Values.Look(ref slamStartSpinDegrees, "slamStartSpinDegrees", 0f);
        }

        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            Thing thing = Payload;
            if (thing == null || state == DunkState.Finished)
            {
                return;
            }

            if (HasAnimatedPresentation
                && (phase == DrawPhase.EnsureInitialized || phase == DrawPhase.Draw))
            {
                SampleDunkPresentation();
            }

            Vector3 pos = DrawPos;
            float spin = Mathf.Repeat(RenderSpinDegrees, 360f);
            Thing_MugirlDunkHead head = thing as Thing_MugirlDunkHead;
            if (head != null)
            {
                if (phase == DrawPhase.Draw)
                {
                    head.DrawHeadAt(pos, spin);
                }
                return;
            }

            if (thing is Pawn)
            {
                MugirlPawnSpinRenderer.Draw((Pawn)thing, phase, pos, spin);
                return;
            }

            if (phase == DrawPhase.Draw)
            {
                Graphic graphic = thing.Graphic?.GetShadowlessGraphic();
                graphic?.Draw(pos, thing.Rotation, thing, spin);
            }
        }

    }
}
