using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class Comp_MooGirlMount : ThingComp, IThingHolder
    {
        public ThingOwner<Thing> innerContainer;
        public bool fireAtWill;
        private int physiologicalTickCounter;
        private int safetyTickCounter;
        private int turretTickCounter;
        private int riderMeleeTickCounter;
        internal int turretBurstCooldownTicksLeft;
        internal LocalTargetInfo turretAimTarget = LocalTargetInfo.Invalid;
        internal int turretAimTicksLeft;
        internal int turretAimTicksTotal;
        internal int turretCastStartTick = -1;
        internal LocalTargetInfo turretLastAttackedTarget = LocalTargetInfo.Invalid;
        internal int turretLastAttackTargetTick;

        IThingHolder IThingHolder.ParentHolder => parent.MapHeld ?? parent.ParentHolder;

        public CompProperties_MooGirlMount Props => (CompProperties_MooGirlMount)props;

        public Pawn MooPawn => parent as Pawn;

        public Pawn MountedPawn => innerContainer?.FirstOrDefault(thing => thing is Pawn) as Pawn;

        public bool HasMountedPawn => MountedPawn != null;

        public Vector3 RiderDrawPos
        {
            get
            {
                Pawn carrier = MooPawn;
                if (carrier == null)
                {
                    return Vector3.zero;
                }

                Vector3 drawPos = carrier.DrawPos;
                Vector3 headOffset = Vector3.zero;
                if (carrier.Drawer?.renderer != null && carrier.story?.bodyType != null)
                {
                    headOffset = carrier.Drawer.renderer.BaseHeadOffsetAt(carrier.Rotation);
                }

                Vector3 result = drawPos + headOffset + MountedPawnUtility.OffsetForRot(Props, carrier.Rotation);
                float altitudeOffset = Props.riderAltitudeOffset;
                if (carrier.Rotation == Rot4.North)
                {
                    altitudeOffset = Props.northRiderAltitudeOffset;
                }
                else if (carrier.Rotation == Rot4.South)
                {
                    altitudeOffset = Props.southRiderAltitudeOffset;
                }

                result.y = drawPos.y + altitudeOffset;
                return result;
            }
        }

        public Vector3 WeaponDrawPos
        {
            get
            {
                Pawn carrier = MooPawn;
                Vector3 pos = RiderDrawPos;
                if (carrier == null)
                {
                    return pos;
                }

                pos += carrier.Rotation.RighthandCell.ToVector3() * 0.14f;
                pos.y += 0.01f;
                return pos;
            }
        }

        public Comp_MooGirlMount()
        {
            MakeContainer();
        }

        public override void Initialize(CompProperties props)
        {
            base.Initialize(props);
            MakeContainer();
            fireAtWill = Props.turretFireAtWillDefault;
        }

        public override void PostPostMake()
        {
            base.PostPostMake();
            MakeContainer();
        }

        private void MakeContainer()
        {
            if (innerContainer == null)
            {
                innerContainer = new ThingOwner<Thing>(this, oneStackOnly: true, LookMode.Deep, removeContentsIfDestroyed: false);
            }
        }

        public bool CanMount(Pawn rider, out string reasonKey)
        {
            reasonKey = null;
            Pawn carrier = MooPawn;
            if (rider == null || carrier == null || rider == carrier)
            {
                reasonKey = "MooGirl.Mount.ReasonInvalid";
                return false;
            }

            if (!MountedPawnUtility.IsMooGirl(carrier))
            {
                reasonKey = "MooGirl.Mount.ReasonTargetNotMooGirl";
                return false;
            }

            if (HasMountedPawn)
            {
                reasonKey = "MooGirl.Mount.ReasonAlreadyHasRider";
                return false;
            }

            if (!carrier.Spawned || carrier.Dead || carrier.Downed || carrier.Destroyed || carrier.IsBurning() || carrier.InMentalState || !carrier.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                reasonKey = "MooGirl.Mount.ReasonTargetBadState";
                return false;
            }

            if (!rider.Spawned || rider.Dead || rider.Downed || rider.Destroyed || rider.IsBurning() || rider.InMentalState || !rider.health.capacities.CapableOf(PawnCapacityDefOf.Moving))
            {
                reasonKey = "MooGirl.Mount.ReasonRiderBadState";
                return false;
            }

            if (rider.RaceProps?.Humanlike != true)
            {
                reasonKey = "MooGirl.Mount.ReasonRiderNotHumanlike";
                return false;
            }

            if (MountedPawnUtility.IsMounted(rider, out _))
            {
                reasonKey = "MooGirl.Mount.ReasonAlreadyMounted";
                return false;
            }

            if (MountedPawnUtility.HasAnyRope(rider) || MountedPawnUtility.HasAnyRope(carrier))
            {
                reasonKey = "MooGirl.Mount.ReasonRoped";
                return false;
            }

            return true;
        }

        public bool TryMount(Pawn rider)
        {
            MakeContainer();
            if (!CanMount(rider, out string reasonKey))
            {
                Messages.Message(("MooGirl.Mount.CannotMount".Translate() + ": " + reasonKey.Translate()).CapitalizeFirst(), rider ?? MooPawn, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            Pawn carrier = MooPawn;
            bool wasSelected = Current.ProgramState == ProgramState.Playing && Find.Selector.IsSelected(rider);
            MountedPawnUtility.PreparePawnForMountContainer(rider);
            MountedPawnUtility.BreakRopes(rider);
            MountedPawnUtility.BreakRopes(carrier);
            rider.DeSpawnOrDeselect();

            if (!innerContainer.TryAdd(rider))
            {
                GenSpawn.Spawn(rider, carrier.Position, carrier.Map);
                if (wasSelected)
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(rider);
                }

                Messages.Message(("MooGirl.Mount.CannotMount".Translate() + ": " + "MooGirl.Mount.ReasonInvalid".Translate()).CapitalizeFirst(), carrier, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            MountedPawnUtility.ClearHiddenJobs(rider);
            MountedPawnCombatTurret.NotifyMounted(this);
            if (wasSelected)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(rider, playSound: false, forceDesignatorDeselect: false);
            }

            Messages.Message("MooGirl.Mount.MountedMessage".Translate(rider.LabelShort, carrier.LabelShort), carrier, MessageTypeDefOf.PositiveEvent);
            return true;
        }

        public bool TryDismount(IntVec3? preferredCell = null, bool sendMessage = true)
        {
            MakeContainer();
            Pawn rider = MountedPawn;
            Pawn carrier = MooPawn;
            if (rider == null || carrier == null)
            {
                return false;
            }

            Map map = carrier.Map;
            if (map == null)
            {
                return false;
            }

            if (!MountedPawnUtility.TryFindDismountCell(carrier, rider, preferredCell, out IntVec3 cell))
            {
                if (sendMessage)
                {
                    Messages.Message("MooGirl.Mount.NoDismountCell".Translate(carrier.LabelShort), carrier, MessageTypeDefOf.RejectInput, historical: false);
                }

                return false;
            }

            bool wasSelected = Current.ProgramState == ProgramState.Playing && Find.Selector.IsSelected(rider);
            if (!innerContainer.TryDrop(rider, cell, map, ThingPlaceMode.Near, out Thing dropped, null, c => MountedPawnUtility.DismountCellValidator(c, carrier, rider, map)))
            {
                if (sendMessage)
                {
                    Messages.Message("MooGirl.Mount.NoDismountCell".Translate(carrier.LabelShort), carrier, MessageTypeDefOf.RejectInput, historical: false);
                }

                return false;
            }

            MountedPawnCombatTurret.NotifyDismounting(this, rider);
            Pawn droppedPawn = dropped as Pawn;
            if (droppedPawn != null && !droppedPawn.Dead && droppedPawn.jobs != null)
            {
                PawnUtility.ForceWait(droppedPawn, 60, carrier);
            }

            if (wasSelected && droppedPawn != null)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(droppedPawn, playSound: false, forceDesignatorDeselect: false);
            }

            if (sendMessage)
            {
                Messages.Message("MooGirl.Mount.DismountedMessage".Translate(rider.LabelShort, carrier.LabelShort), carrier, MessageTypeDefOf.NeutralEvent);
            }

            return true;
        }

        public bool TryEmergencyDismountNear(IntVec3 cell, Map map)
        {
            MakeContainer();
            Pawn rider = MountedPawn;
            if (rider == null || map == null)
            {
                return false;
            }

            bool wasSelected = Current.ProgramState == ProgramState.Playing && Find.Selector.IsSelected(rider);
            if (!innerContainer.TryDrop(rider, cell, map, ThingPlaceMode.Near, out Thing dropped, null, null, playDropSound: false))
            {
                return false;
            }

            MountedPawnCombatTurret.NotifyDismounting(this, rider);
            Pawn droppedPawn = dropped as Pawn;
            if (droppedPawn != null && !droppedPawn.Dead && droppedPawn.jobs != null)
            {
                PawnUtility.ForceWait(droppedPawn, 60, MooPawn);
            }

            if (wasSelected && droppedPawn != null)
            {
                Find.Selector.ClearSelection();
                Find.Selector.Select(droppedPawn, playSound: false, forceDesignatorDeselect: false);
            }

            return true;
        }

        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);
            Pawn rider = MountedPawn;
            if (rider == null)
            {
                return;
            }

            physiologicalTickCounter += delta;
            safetyTickCounter += delta;
            turretTickCounter += delta;
            riderMeleeTickCounter += delta;
            MountedPawnCombatTurret.VerbTick(this, delta);
            MountedPawnCombatTurret.TickAim(this, delta);

            if (Props.tickPhysiology && physiologicalTickCounter >= Props.physiologicalTickInterval)
            {
                int tickDelta = physiologicalTickCounter;
                physiologicalTickCounter = 0;
                MountedPawnUtility.PhysiologyTick(rider, tickDelta);
                if (rider.Dead)
                {
                    TryDismount(sendMessage: false);
                    return;
                }
            }

            if (Props.autoDismount && safetyTickCounter >= Props.safetyCheckInterval)
            {
                safetyTickCounter = 0;
                if (MountedPawnUtility.ShouldAutoDismount(rider, MooPawn, out string reasonKey))
                {
                    string reason = reasonKey.NullOrEmpty() ? "MooGirl.Mount.ReasonInvalid".Translate().ToString() : reasonKey.Translate().ToString();
                    if (TryDismount(sendMessage: false))
                    {
                        Messages.Message("MooGirl.Mount.AutoDismountMessage".Translate(rider.LabelShort, reason), MooPawn, MessageTypeDefOf.NeutralEvent);
                    }

                    return;
                }
            }

            if (turretTickCounter >= Props.turretTickInterval)
            {
                int tickDelta = turretTickCounter;
                turretTickCounter = 0;
                MountedPawnCombatTurret.Tick(this, tickDelta);
            }

            if (riderMeleeTickCounter >= Props.turretTickInterval)
            {
                riderMeleeTickCounter = 0;
                MountedPawnMeleeSupport.Tick(this);
            }
        }

        public override void PostDeSpawn(Map map, DestroyMode mode = DestroyMode.Vanish)
        {
            base.PostDeSpawn(map, mode);
            if (HasMountedPawn && mode != DestroyMode.WillReplace && map != null)
            {
                TryDropAt(parent.PositionHeld, map);
            }
        }

        public override void PostDestroy(DestroyMode mode, Map previousMap)
        {
            base.PostDestroy(mode, previousMap);
            if (HasMountedPawn && previousMap != null)
            {
                TryDropAt(parent.PositionHeld, previousMap);
            }
            else if (HasMountedPawn)
            {
                MountedPawnCombatTurret.NotifyDismounting(this);
                innerContainer.ClearAndDestroyContentsOrPassToWorld(mode);
            }
        }

        private void TryDropAt(IntVec3 cell, Map map)
        {
            Pawn rider = MountedPawn;
            if (rider == null || map == null)
            {
                return;
            }

            MountedPawnCombatTurret.NotifyDismounting(this);
            innerContainer.TryDrop(rider, cell, map, ThingPlaceMode.Near, out Thing _);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Deep.Look(ref innerContainer, "innerContainer", this);
            Scribe_Values.Look(ref fireAtWill, "fireAtWill", Props?.turretFireAtWillDefault ?? true);
            Scribe_Values.Look(ref physiologicalTickCounter, "physiologicalTickCounter", 0);
            Scribe_Values.Look(ref safetyTickCounter, "safetyTickCounter", 0);
            Scribe_Values.Look(ref turretTickCounter, "turretTickCounter", 0);
            Scribe_Values.Look(ref riderMeleeTickCounter, "riderMeleeTickCounter", 0);
            Scribe_Values.Look(ref turretBurstCooldownTicksLeft, "turretBurstCooldownTicksLeft", 0);
            Scribe_TargetInfo.Look(ref turretAimTarget, "turretAimTarget");
            Scribe_Values.Look(ref turretAimTicksLeft, "turretAimTicksLeft", 0);
            Scribe_Values.Look(ref turretAimTicksTotal, "turretAimTicksTotal", 0);
            Scribe_Values.Look(ref turretCastStartTick, "turretCastStartTick", -1);
            Scribe_TargetInfo.Look(ref turretLastAttackedTarget, "turretLastAttackedTarget");
            Scribe_Values.Look(ref turretLastAttackTargetTick, "turretLastAttackTargetTick", 0);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                MakeContainer();
            }
        }

        public override void PostSpawnSetup(bool respawningAfterLoad)
        {
            base.PostSpawnSetup(respawningAfterLoad);
            if (HasMountedPawn)
            {
                MountedPawnCombatTurret.NotifyMounted(this);
            }
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            foreach (Gizmo gizmo in base.CompGetGizmosExtra())
            {
                yield return gizmo;
            }

            Pawn rider = MountedPawn;
            if (rider == null)
            {
                yield break;
            }

            yield return new Command_Action
            {
                defaultLabel = "MooGirl.Mount.SelectRider".Translate(),
                defaultDesc = "MooGirl.Mount.SelectRiderDesc".Translate(),
                icon = TexCommand.SelectCarriedPawn,
                action = delegate
                {
                    Find.Selector.ClearSelection();
                    Find.Selector.Select(rider);
                }
            };

            yield return new Command_Action
            {
                defaultLabel = "MooGirl.Mount.DismountRider".Translate(),
                defaultDesc = "MooGirl.Mount.DismountRiderDesc".Translate(),
                icon = TexCommand.DropCarriedPawn,
                action = delegate
                {
                    TryDismount();
                }
            };

            yield return new Command_Toggle
            {
                defaultLabel = fireAtWill ? "MooGirl.Mount.FireAtWill".Translate() : "MooGirl.Mount.HoldFire".Translate(),
                defaultDesc = fireAtWill ? "MooGirl.Mount.FireAtWillDesc".Translate() : "MooGirl.Mount.HoldFireDesc".Translate(),
                icon = fireAtWill ? TexCommand.FireAtWill : TexCommand.CannotShoot,
                isActive = () => fireAtWill,
                toggleAction = delegate
                {
                    fireAtWill = !fireAtWill;
                }
            };
        }

        public override string CompInspectStringExtra()
        {
            Pawn rider = MountedPawn;
            if (rider == null)
            {
                return null;
            }

            return "MooGirl.Mount.InspectRider".Translate(rider.LabelShort);
        }

        public void GetChildHolders(List<IThingHolder> outChildren)
        {
            ThingOwnerUtility.AppendThingHoldersFromThings(outChildren, GetDirectlyHeldThings());
        }

        public ThingOwner GetDirectlyHeldThings()
        {
            MakeContainer();
            return innerContainer;
        }
    }
}
