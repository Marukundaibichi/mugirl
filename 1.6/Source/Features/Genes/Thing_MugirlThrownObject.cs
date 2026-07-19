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
        private const float SpinDegreesPerTick = 42f;

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
        private IntVec3 lastFlightCell = IntVec3.Invalid;
        private bool resolving;

        public Pawn Carrier => carrier;
        public bool IsHeld => state == ThrowState.Held;
        public bool ShouldTickContents => state != ThrowState.Resolving;
        public ThingOwner SearchableContents => innerContainer;

        private Thing HeldThing => innerContainer != null && innerContainer.Count > 0 ? innerContainer[0] : null;
        private float MaxThrowDistance => MugirlThrowUtility.MaxThrowDistance(carrier, mass);

        public override Vector3 DrawPos
        {
            get
            {
                if (state == ThrowState.Flying)
                {
                    return FlyingDrawPos;
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

        private Vector3 FlyingDrawPos
        {
            get
            {
                float progress = flightTicks <= 0 ? 1f : Mathf.Clamp01((float)ticksFlying / flightTicks);
                Vector3 ground = Vector3.Lerp(flightStartDraw, destination.ToVector3Shifted(), progress);
                float distance = flightStartGround.ToIntVec3().DistanceTo(destination);
                float arcHeight = Mathf.Clamp(distance * 0.32f, 2.2f, 6.5f);
                float height = 4f * progress * (1f - progress) * arcHeight;
                ground.z += height * 0.55f;
                ground.y = AltitudeLayer.MoteOverhead.AltitudeFor() + height;
                return ground;
            }
        }

        public Thing_MugirlThrownObject()
        {
            innerContainer = new ThingOwner<Thing>(this, oneStackOnly: true);
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

            bool playedSound = false;
            foreach (IntVec3 cell in GenSight.PointsOnLineOfSight(from, to))
            {
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
                heldTicks = 0;
                BeginAiming();
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
                heldTicks = 0;
                BeginAiming();
                return;
            }

            if (resetCooldown)
            {
                sourceAbility?.ResetCooldown();
            }
            Destroy();
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

            Vector3 pos = DrawPos;
            if (thing is Pawn)
            {
                if (state == ThrowState.Flying)
                {
                    MugirlPawnSpinRenderer.Draw((Pawn)thing, phase, pos, (ticksFlying * SpinDegreesPerTick) % 360f);
                }
                else
                {
                    thing.DynamicDrawPhaseAt(phase, pos, flip);
                }
                return;
            }

            if (phase == DrawPhase.Draw)
            {
                float spin = state == ThrowState.Flying ? (ticksFlying * SpinDegreesPerTick) % 360f : 0f;
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

        public TargetingParameters targetParams => new TargetingParameters
        {
            canTargetLocations = true,
            canTargetPawns = false,
            canTargetBuildings = false,
            canTargetItems = false,
            canTargetFires = false
        };

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
