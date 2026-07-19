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
            Finished
        }

        private const int SpinTicks = 18;
        private const int FlightTicksBeforeImpact = 31;
        private const int FinishedLifetimeTicks = 90;

        private ThingOwner<Thing> innerContainer;
        private Pawn carrier;
        private Ability sourceAbility;
        private DunkState state;
        private IntVec3 originalPosition;
        private Rot4 originalRotation = Rot4.North;
        private float mass;
        private IntVec3 impactCell;
        private IntVec3 landingCell;
        private Vector3 launchDrawPos;
        private int stateTicks;
        private int runningTicks;
        private bool resolving;

        private Thing Payload => innerContainer != null && innerContainer.Count > 0 ? innerContainer[0] : null;

        public Pawn Carrier => carrier;
        public bool HasPayload => Payload != null;
        public bool SequenceStarted => state != DunkState.Dribbling;
        public bool Completed => state == DunkState.Finished;
        public bool ShouldTickContents => state != DunkState.Finished;
        public ThingOwner SearchableContents => innerContainer;

        public override Vector3 DrawPos
        {
            get
            {
                switch (state)
                {
                    case DunkState.Spin:
                        return SpinDrawPos;
                    case DunkState.Flight:
                        return FlightDrawPos;
                    case DunkState.Slam:
                        return SlamDrawPos;
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

        private Vector3 SpinDrawPos
        {
            get
            {
                Vector3 center = carrier != null && carrier.Spawned ? carrier.DrawPos : launchDrawPos;
                float radians = stateTicks * 36f * Mathf.Deg2Rad;
                center.x += Mathf.Cos(radians) * 0.55f;
                center.z += 1.05f + Mathf.Sin(radians) * 0.30f;
                center.y = AltitudeLayer.MoteOverhead.AltitudeFor() + 0.06f;
                return center;
            }
        }

        private Vector3 FlightDrawPos
        {
            get
            {
                float t = Mathf.Clamp01(stateTicks / (float)FlightTicksBeforeImpact);
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

        public Thing_MugirlDunkProp()
        {
            innerContainer = new ThingOwner<Thing>(this, oneStackOnly: true);
        }

        public static bool TryCreate(Pawn caster, Thing target, Ability ability, out Thing_MugirlDunkProp prop)
        {
            prop = null;
            string reason;
            if (!MugirlThrowUtility.CanPickUp(caster, target, out reason) || Mugirl_DefOf.Mugirl_DunkProp == null)
            {
                return false;
            }

            Map map = caster.Map;
            IntVec3 origin = target.Position;
            Rot4 rotation = target.Rotation;
            Thing_MugirlDunkProp controller = ThingMaker.MakeThing(Mugirl_DefOf.Mugirl_DunkProp) as Thing_MugirlDunkProp;
            if (controller == null)
            {
                return false;
            }

            controller.carrier = caster;
            controller.sourceAbility = ability;
            controller.state = DunkState.Dribbling;
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

            prop = controller;
            return true;
        }

        public void NotifyRunning()
        {
            runningTicks = 3;
        }

        public bool BeginDunk(IntVec3 destination)
        {
            if (state != DunkState.Dribbling || Payload == null || Map == null || !destination.InBounds(Map))
            {
                return false;
            }

            impactCell = destination;
            landingCell = FindLandingCell(destination);
            launchDrawPos = DribbleDrawPos;
            state = DunkState.Spin;
            stateTicks = 0;
            DefDatabase<SoundDef>.GetNamedSilentFail("Longjump_Jump")?.PlayOneShot(new TargetInfo(carrier.Position, Map));
            VerbProperties jumpProps = new VerbProperties
            {
                soundLanding = DefDatabase<SoundDef>.GetNamedSilentFail("Longjump_Land")
            };
            if (JobDriver_CastCharge.DoJump(carrier, landingCell, jumpProps, sourceAbility, impactCell, Mugirl_DefOf.Mugirl_DunkFlyer))
            {
                return true;
            }

            state = DunkState.Dribbling;
            stateTicks = 0;
            return false;
        }

        protected override void TickInterval(int delta)
        {
            base.TickInterval(delta);
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

            launchDrawPos = SpinDrawPos;
            state = DunkState.Flight;
            stateTicks = 0;
        }

        private void TickFlight()
        {
            if (stateTicks < FlightTicksBeforeImpact)
            {
                return;
            }

            DefDatabase<SoundDef>.GetNamedSilentFail("MetalHitImportant")?.PlayOneShot(new TargetInfo(impactCell, Map));
            ResolveSmash();
        }

        private void TickSlam()
        {
            // 兼容上一版恰好保存在直坠阶段的存档；新版轨迹不再进入该状态。
            ResolveSmash();
        }

        private void ResolveSmash()
        {
            if (resolving || Map == null)
            {
                return;
            }

            resolving = true;
            Map map = Map;
            Thing payload = TryPlacePayload(impactCell, map);
            if (payload == null && HasPayload)
            {
                resolving = false;
                TryDropSafely();
                return;
            }

            int damage = Mathf.RoundToInt(MugirlThrowUtility.ImpactDamage(mass, 18f) * 1.7f);
            float radius = Mathf.Clamp(MugirlThrowUtility.ExplosionRadius(damage) * 1.12f, 2.8f, 7.2f);
            DamageDef damageDef = Mugirl_DefOf.Mugirl_ThrownImpact ?? DamageDefOf.Bomb;

            MugirlPunisherDeathEffectUtility.Play(impactCell.ToVector3Shifted(), map, 1.35f);
            List<Thing> ignored = new List<Thing> { this };
            if (carrier != null)
            {
                ignored.Add(carrier);
            }
            ShatterPayload(payload, damage);
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

            state = DunkState.Finished;
            stateTicks = 0;
            resolving = false;
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
            }
        }

        private IntVec3 FindLandingCell(IntVec3 center)
        {
            Map map = Map;
            Vector3 awayVector = (carrier.Position - center).ToVector3().normalized;
            IntVec3 preferred = center + awayVector.ToIntVec3();
            if (preferred == center)
            {
                preferred = center + IntVec3.South;
            }

            if (JumpUtility.ValidJumpTarget(carrier, map, preferred))
            {
                return preferred;
            }

            int count = GenRadial.NumCellsInRadius(2.4f);
            for (int i = 0; i < count; i++)
            {
                IntVec3 cell = center + GenRadial.RadialPattern[i];
                if (cell != center && JumpUtility.ValidJumpTarget(carrier, map, cell))
                {
                    return cell;
                }
            }
            return carrier.Position;
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
            Scribe_Values.Look(ref state, "state", DunkState.Dribbling);
            Scribe_Values.Look(ref originalPosition, "originalPosition");
            Scribe_Values.Look(ref originalRotation, "originalRotation", Rot4.North);
            Scribe_Values.Look(ref mass, "mass", 0f);
            Scribe_Values.Look(ref impactCell, "impactCell");
            Scribe_Values.Look(ref landingCell, "landingCell");
            Scribe_Values.Look(ref launchDrawPos, "launchDrawPos");
            Scribe_Values.Look(ref stateTicks, "stateTicks", 0);
            Scribe_Values.Look(ref runningTicks, "runningTicks", 0);
            Scribe_Values.Look(ref resolving, "resolving", false);
        }

        public override void DynamicDrawPhaseAt(DrawPhase phase, Vector3 drawLoc, bool flip = false)
        {
            Thing thing = Payload;
            if (thing == null || state == DunkState.Finished)
            {
                return;
            }

            Vector3 pos = DrawPos;
            float spin = state == DunkState.Dribbling
                ? MugirlTickUtility.CurrentGameTickOrFallback(0) * 24f
                : stateTicks * (state == DunkState.Slam ? 78f : 46f);
            if (thing is Pawn)
            {
                MugirlPawnSpinRenderer.Draw((Pawn)thing, phase, pos, spin % 360f);
                return;
            }

            if (phase == DrawPhase.Draw)
            {
                Graphic graphic = thing.Graphic?.GetShadowlessGraphic();
                graphic?.Draw(pos, thing.Rotation, thing, spin % 360f);
            }
        }

    }
}
