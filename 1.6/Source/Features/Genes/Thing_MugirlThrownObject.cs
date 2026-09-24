using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Mugirl
{
    public class Thing_MugirlThrownObject : Thing, IThingHolderTickable, IThingHolder, ITargetingSource
    {
        private enum ThrowState : byte
        {
            Held,
            Flying,
            Resolving
        }

        private const int HoldTimeoutTicks = 300;
        // 保留原有空中连转数圈的转速；绘制使用 visualTicks 逐帧补间。
        private const float SpinDegreesPerTick = 42f;
        // 落点放置失败后的重试退避：首退 30 tick，逐次翻倍，上限 120 tick。
        // 避免不可放置载荷每 tick 触发全径向落点扫描。
        private const int FirstDropRetryDelayTicks = 30;
        private const int MaxDropRetryDelayTicks = 120;
        // 瞄准读数与射程校验共用；携带上限在瞄准期内基本不变，按 30 tick 失效。
        private const int MaxThrowDistanceCacheTicks = 30;

        private ThingOwner<Thing> innerContainer;
        private Pawn carrier;
        private Ability sourceAbility;
        private ThrowState state;
        private IntVec3 originalPosition;
        private Rot4 originalRotation = Rot4.North;
        private int heldTicks;
        private float mass;
        private IntVec3 destination;
        private Vector3 flightStartGround;
        private Vector3 flightStartDraw;
        private int flightTicks;
        private int ticksFlying;
        // 仅供绘制补间；不存档，不参与屋顶、命中和落点判定。
        private float visualTicks;
        private int visualFrame = -1;
        private IntVec3 lastFlightCell = IntVec3.Invalid;
        private bool resolving;
        private int dropRetryDelayTicks;
        private int dropRetryNotBeforeTick;
        private float cachedMaxThrowDistance = -1f;
        private int cachedMaxThrowDistanceTick = int.MinValue;
        private TargetingParameters cachedTargetParams;

        public Pawn Carrier => carrier;
        public bool IsHeld => state == ThrowState.Held;
        public bool ShouldTickContents => state != ThrowState.Resolving;
        public ThingOwner SearchableContents => innerContainer;

        private Thing HeldThing => innerContainer != null && innerContainer.Count > 0 ? innerContainer[0] : null;
        // 瞄准期间 Targeter 与 GUI 每 tick 多次读取；GetStatValue 走完整 Stat 管线，
        // 这里按短 TTL 缓存，携带上限变化最迟 30 tick 后反映。
        private float MaxThrowDistance
        {
            get
            {
                int tick = MugirlTickUtility.CurrentGameTickOrFallback(0);
                if (cachedMaxThrowDistanceTick >= 0 && tick < cachedMaxThrowDistanceTick + MaxThrowDistanceCacheTicks)
                {
                    return cachedMaxThrowDistance;
                }

                cachedMaxThrowDistance = MugirlThrowUtility.MaxThrowDistance(carrier, mass);
                cachedMaxThrowDistanceTick = tick;
                return cachedMaxThrowDistance;
            }
        }

        public override Vector3 DrawPos
        {
            get
            {
                if (state == ThrowState.Flying)
                {
                    return FlyingDrawPosAt(visualTicks);
                }

                return HeldDrawPos;
            }
        }

        private Vector3 HeldDrawPos
        {
            get
            {
                Vector3 pos = carrier != null && carrier.Spawned ? carrier.DrawPos : originalPosition.ToVector3Shifted();
                pos.z += 1.05f + Mathf.Sin(MugirlTickUtility.CurrentGameTickOrFallback(0) * 0.12f) * 0.08f;
                pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
                return pos;
            }
        }

        private Vector3 FlyingGroundPos
        {
            get
            {
                float progress = flightTicks <= 0 ? 1f : Mathf.Clamp01((float)ticksFlying / flightTicks);
                return Vector3.Lerp(flightStartGround, destination.ToVector3Shifted(), progress);
            }
        }

        internal float FlyingRenderTick => visualTicks;

        internal void UpdateFlightPresentation(float frameSeconds, float tickRateMultiplier, bool paused)
        {
            if (state != ThrowState.Flying || paused)
            {
                return;
            }

            // 仅追随已推进的游戏 tick，不预测屋顶穿透和命中后的未来位置。
            float frameTicks = Mathf.Max(0f, frameSeconds) * 60f * Mathf.Max(0f, tickRateMultiplier);
            visualTicks = Mathf.Clamp(visualTicks + frameTicks, 0f, ticksFlying);
        }

        private void SampleFlightPresentation()
        {
            // EnsureInitialized 和 Draw 在主线程运行；ParallelPreDraw 只读取 visualTicks。
            int frame = Time.frameCount;
            if (visualFrame == frame)
            {
                return;
            }

            if ((visualFrame < 0 && ticksFlying > 1)
                || (visualFrame >= 0 && frame > visualFrame + 1))
            {
                // 离开视野后再次绘制时追上模拟进度，避免飞物重现于旧位置。
                visualTicks = Mathf.Max(visualTicks, ticksFlying - 1f);
            }
            visualFrame = frame;
            bool running = MugirlTickUtility.TryGetPresentationTickRate(out float tickRateMultiplier);
            UpdateFlightPresentation(Time.unscaledDeltaTime, tickRateMultiplier, !running);
        }

        private Vector3 FlyingDrawPosAt(float renderTick)
        {
            float progress = flightTicks <= 0 ? 1f : Mathf.Clamp01(renderTick / flightTicks);
            Vector3 ground = Vector3.Lerp(flightStartDraw, destination.ToVector3Shifted(), progress);
            float distance = flightStartGround.ToIntVec3().DistanceTo(destination);
            float arcHeight = Mathf.Clamp(distance * 0.32f, 2.2f, 6.5f);
            float height = 4f * progress * (1f - progress) * arcHeight;
            ground.z += height * 0.55f;
            ground.y = AltitudeLayer.MoteOverhead.AltitudeFor() + height;
            return ground;
        }

        public Thing_MugirlThrownObject()
        {
            innerContainer = new ThingOwner<Thing>(this, oneStackOnly: true);
        }

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            if (state == ThrowState.Flying)
            {
                visualTicks = ticksFlying;
                visualFrame = -1;
            }
        }

        public static bool TryCreate(Pawn caster, Thing target, Ability ability)
        {
            string reason;
            if (!MugirlThrowUtility.CanPickUp(caster, target, out reason) || Mugirl_DefOf.Mugirl_ThrowController == null)
            {
                return false;
            }

            Map map = caster.Map;
            IntVec3 origin = target.Position;
            Rot4 rotation = target.Rotation;
            Thing_MugirlThrownObject controller = ThingMaker.MakeThing(Mugirl_DefOf.Mugirl_ThrowController) as Thing_MugirlThrownObject;
            if (controller == null)
            {
                return false;
            }

            controller.carrier = caster;
            controller.sourceAbility = ability;
            controller.state = ThrowState.Held;
            controller.originalPosition = origin;
            controller.originalRotation = rotation;
            controller.mass = MugirlThrowUtility.EffectiveMass(target);

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

        private void BeginAiming()
        {
            if (state != ThrowState.Held || carrier == null || !carrier.Spawned)
            {
                SafeDrop(resetCooldown: true);
                return;
            }

            if (!MugirlGameUtility.TryBeginTargeting(
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
            bool targetingThis = MugirlGameUtility.IsTargetingSource(this);
            bool otherPlayerOrder = heldTicks > 2
                && carrier?.CurJob != null
                && carrier.CurJob.playerForced
                && carrier.CurJob.ability != sourceAbility;
            if (carrier == null || !carrier.Spawned || carrier.Map != Map || carrier.Dead || heldTicks >= HoldTimeoutTicks || !targetingThis || otherPlayerOrder)
            {
                SafeDrop(resetCooldown: true);
            }
        }

        private void TickFlying(int delta)
        {
            IntVec3 previousCell = lastFlightCell.IsValid ? lastFlightCell : flightStartGround.ToIntVec3();
            ticksFlying += delta;
            IntVec3 currentCell = FlyingGroundPos.ToIntVec3();
            PunchThroughRoofs(previousCell, currentCell);
            lastFlightCell = currentCell;

            if (ticksFlying % 4 < delta && currentCell.InBounds(Map))
            {
                FleckMaker.ThrowDustPuff(FlyingGroundPos + Gen.RandomHorizontalVector(0.18f), Map, 0.55f);
            }

            if (ticksFlying >= flightTicks)
            {
                ResolveImpact();
            }
        }

        private void PunchThroughRoofs(IntVec3 from, IntVec3 to)
        {
            if (Map == null || !from.IsValid || !to.IsValid)
            {
                return;
            }

            // 飞行每 tick 调用；等价复刻 GenSight.PointsOnLineOfSight（含起终点）写入复用缓冲，
            // 避免原版迭代器每 tick 分配枚举器。
            LineCellsNonAlloc(from, to, tmpRoofLineCells);
            bool playedSound = false;
            for (int i = 0; i < tmpRoofLineCells.Count; i++)
            {
                IntVec3 cell = tmpRoofLineCells[i];
                if (!cell.InBounds(Map) || !cell.Roofed(Map))
                {
                    continue;
                }

                Map.roofGrid.SetRoof(cell, null);
                FleckMaker.ThrowDustPuff(cell.ToVector3Shifted() + Gen.RandomHorizontalVector(0.35f), Map, 2.4f);
                if (!playedSound)
                {
                    SoundDefOf.Roof_Collapse.PlayOneShot(new TargetInfo(cell, Map));
                    playedSound = true;
                }
            }
        }

        // StaticCacheLifecycle: 屋顶穿透专用走线缓冲；仅在单线程 tick 内使用，
        // 每次调用先清空，方法返回后不再持有任何 Thing 引用。
        private static readonly List<IntVec3> tmpRoofLineCells = new List<IntVec3>();

        private static void LineCellsNonAlloc(IntVec3 start, IntVec3 end, List<IntVec3> outCells)
        {
            outCells.Clear();
            bool sideOnEqual = start.x != end.x ? start.x < end.x : start.z < end.z;
            int dx = Mathf.Abs(end.x - start.x);
            int dz = Mathf.Abs(end.z - start.z);
            int x = start.x;
            int z = start.z;
            int n = 1 + dx + dz;
            int xInc = end.x > start.x ? 1 : -1;
            int zInc = end.z > start.z ? 1 : -1;
            int error = dx - dz;
            dx *= 2;
            dz *= 2;
            while (n > 0)
            {
                outCells.Add(new IntVec3(x, 0, z));
                if (error > 0 || (error == 0 && sideOnEqual))
                {
                    x += xInc;
                    error -= dz;
                }
                else
                {
                    z += zInc;
                    error += dx;
                }
                n--;
            }
        }

        private void Launch(IntVec3 targetCell)
        {
            if (!CanHitTarget(targetCell))
            {
                return;
            }

            float distance = carrier.Position.DistanceTo(targetCell);
            state = ThrowState.Flying;
            destination = targetCell;
            flightStartGround = carrier.DrawPos;
            flightStartDraw = HeldDrawPos;
            flightTicks = Mathf.RoundToInt(Mathf.Clamp(14f + distance * 2.2f, 22f, 75f));
            ticksFlying = 0;
            visualTicks = 0f;
            visualFrame = -1;
            lastFlightCell = carrier.Position;
            DefDatabase<SoundDef>.GetNamedSilentFail("Longjump_Jump")?.PlayOneShot(new TargetInfo(carrier.Position, Map));
        }

        private void ResolveImpact()
        {
            if (resolving || Map == null)
            {
                return;
            }

            resolving = true;
            state = ThrowState.Resolving;
            Map map = Map;
            float distance = flightStartGround.ToIntVec3().DistanceTo(destination);
            int damage = MugirlThrowUtility.ImpactDamage(mass, distance);
            float radius = MugirlThrowUtility.ExplosionRadius(damage);
            DamageDef impactDef = Mugirl_DefOf.Mugirl_ThrownImpact ?? DamageDefOf.Bomb;

            GenExplosion.DoExplosion(
                center: destination,
                map: map,
                radius: radius,
                damType: impactDef,
                instigator: carrier,
                damAmount: damage,
                armorPenetration: damage * 0.012f,
                damageFalloff: true,
                ignoredThings: new List<Thing> { this },
                screenShakeFactor: Mathf.Clamp(radius / 3f, 0.8f, 2f));

            for (int i = 0; i < 5; i++)
            {
                FleckMaker.ThrowDustPuff(destination.ToVector3Shifted() + Gen.RandomHorizontalVector(radius * 0.45f), map, Rand.Range(1.4f, 2.8f));
            }

            Thing landed = TryPlaceHeldThing(destination, map);
            if (landed == null)
            {
                resolving = false;
                state = ThrowState.Held;
                // 没有安全落点时按退避节奏在后续 Tick 重试归还，避免每 tick 全径向重扫。
                ScheduleDropRetry(MugirlTickUtility.CurrentGameTickOrFallback(0));
                return;
            }

            landed.TakeDamage(new DamageInfo(DamageDefOf.Crush, damage, damage * 0.012f, -1f, carrier));
            TryBreakFurnitureApart(landed, damage);
            Destroy();
        }

        private static void TryBreakFurnitureApart(Thing landed, int damage)
        {
            if (!(landed is Building) || landed.Destroyed || !landed.def.Minifiable || landed.MaxHitPoints <= 0)
            {
                return;
            }

            float damageFraction = (float)damage / landed.MaxHitPoints;
            float chance = Mathf.Clamp01((damageFraction - 0.15f) * 0.75f);
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
            int now = MugirlTickUtility.CurrentGameTickOrFallback(0);
            if (dropRetryNotBeforeTick > now)
            {
                return;
            }

            resolving = true;
            state = ThrowState.Resolving;
            MugirlGameUtility.TryStopTargeting(this);

            Map map = Map;
            IntVec3 preferred = carrier != null && carrier.Spawned && carrier.Map == map ? carrier.Position : originalPosition;
            Thing dropped = TryPlaceHeldThing(preferred, map);
            if (dropped == null)
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
            dropRetryDelayTicks = dropRetryDelayTicks <= 0
                ? FirstDropRetryDelayTicks
                : Mathf.Min(MaxDropRetryDelayTicks, dropRetryDelayTicks * 2);
            dropRetryNotBeforeTick = now + dropRetryDelayTicks;
        }

        private Thing TryPlaceHeldThing(IntVec3 preferred, Map map)
        {
            Thing thing = HeldThing;
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
                MugirlLog.WarningOnce("Throw.PlaceFailed." + thing.thingIDNumber, "Mugirl.Throw.PlaceFailed".Translate(thing.LabelCap, ex.Message).ToString());
                return null;
            }
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

        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            Thing thing = HeldThing;
            if (thing == null)
            {
                return;
            }

            if (state == ThrowState.Flying && (phase == DrawPhase.EnsureInitialized || phase == DrawPhase.Draw))
            {
                SampleFlightPresentation();
            }

            float renderTick = state == ThrowState.Flying ? visualTicks : 0f;
            Vector3 pos = state == ThrowState.Flying ? FlyingDrawPosAt(renderTick) : HeldDrawPos;
            float spin = state == ThrowState.Flying ? (renderTick * SpinDegreesPerTick) % 360f : 0f;
            if (thing is Pawn)
            {
                if (state == ThrowState.Flying)
                {
                    MugirlPawnSpinRenderer.Draw((Pawn)thing, phase, pos, spin);
                }
                else
                {
                    thing.DynamicDrawPhaseAt(phase, pos, flip);
                }
                return;
            }

            if (phase == DrawPhase.Draw)
            {
                Graphic graphic = thing.Graphic?.GetShadowlessGraphic();
                graphic?.Draw(pos, thing.Rotation, thing, spin);
            }
        }

        public bool CasterIsPawn => true;
        public bool IsMeleeAttack => false;
        public bool Targetable => true;
        public bool MultiSelect => false;
        public bool HidePawnTooltips => true;
        public Thing Caster => carrier;
        public Pawn CasterPawn => carrier;
        public Verb GetVerb => null;
        public Texture2D UIIcon => sourceAbility?.def?.uiIcon ?? BaseContent.BadTex;
        public ITargetingSource DestinationSelector => null;

        public TargetingParameters targetParams
        {
            get
            {
                // Targeter 每 GUI 事件读取；参数在瞄准会话内不变，构造一次复用。
                if (cachedTargetParams == null)
                {
                    cachedTargetParams = new TargetingParameters
                    {
                        canTargetLocations = true,
                        canTargetPawns = false,
                        canTargetBuildings = false,
                        canTargetItems = false,
                        canTargetFires = false
                    };
                }
                return cachedTargetParams;
            }
        }

        public bool CanHitTarget(LocalTargetInfo target)
        {
            return target.IsValid
                && target.Cell.InBounds(Map)
                && carrier != null
                && carrier.Spawned
                && carrier.Position.DistanceTo(target.Cell) <= MaxThrowDistance;
        }

        public bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
        {
            if (CanHitTarget(target))
            {
                return true;
            }

            if (showMessages)
            {
                Messages.Message("Mugirl.Throw.OutOfRange".Translate(MaxThrowDistance.ToString("0.#")), carrier, MessageTypeDefOf.RejectInput, historical: false);
            }
            return false;
        }

        public void DrawHighlight(LocalTargetInfo target)
        {
            if (carrier != null && carrier.Spawned)
            {
                GenDraw.DrawRadiusRing(carrier.Position, MaxThrowDistance);
            }
            if (target.IsValid)
            {
                int damage = MugirlThrowUtility.ImpactDamage(mass, carrier.Position.DistanceTo(target.Cell));
                GenDraw.DrawTargetHighlight(target);
                GenDraw.DrawRadiusRing(target.Cell, MugirlThrowUtility.ExplosionRadius(damage), new Color(1f, 0.35f, 0.15f));
            }
        }

        public void OrderForceTarget(LocalTargetInfo target)
        {
            if (ValidateTarget(target))
            {
                Launch(target.Cell);
            }
        }

        public void OnGUI(LocalTargetInfo target)
        {
            GenUI.DrawMouseAttachment(CanHitTarget(target) ? UIIcon : TexCommand.CannotShoot);
            if (target.IsValid && carrier != null)
            {
                float distance = carrier.Position.DistanceTo(target.Cell);
                int damage = MugirlThrowUtility.ImpactDamage(mass, distance);
                float radius = MugirlThrowUtility.ExplosionRadius(damage);
                Widgets.MouseAttachedLabel("Mugirl.Throw.AimReadout".Translate(damage, radius.ToString("0.#"), MaxThrowDistance.ToString("0.#")));
            }
        }
    }
}
