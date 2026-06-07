using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public partial class Comp_MooGirlMount : ThingComp, IThingHolder
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

        public CompProperties_MooGirlMount Props => props as CompProperties_MooGirlMount;

        public Pawn MooPawn => parent as Pawn;

        public Pawn MountedPawn
        {
            get
            {
                if (innerContainer == null)
                {
                    return null;
                }

                for (int i = 0; i < innerContainer.Count; i++)
                {
                    if (innerContainer[i] is Pawn pawn)
                    {
                        return pawn;
                    }
                }

                return null;
            }
        }

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

                CompProperties_MooGirlMount mountProps = Props;
                Vector3 result = drawPos + headOffset + MountedPawnUtility.OffsetForRot(mountProps, carrier.Rotation);
                if (mountProps == null)
                {
                    result.y = drawPos.y;
                    return result;
                }

                float altitudeOffset = mountProps.riderAltitudeOffset;
                if (carrier.Rotation == Rot4.North)
                {
                    altitudeOffset = mountProps.northRiderAltitudeOffset;
                }
                else if (carrier.Rotation == Rot4.South)
                {
                    altitudeOffset = mountProps.southRiderAltitudeOffset;
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

                pos += MountedPawnUtility.MountedWeaponSideOffset(carrier.Rotation, 0.14f);
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
            fireAtWill = Props?.turretFireAtWillDefault ?? true;
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
            return MountEligibilityService.CanMount(this, rider, out reasonKey);
        }

        public bool TryMount(Pawn rider)
        {
            MakeContainer();
            if (!CanMount(rider, out string reasonKey))
            {
                Messages.Message("MooGirl.Mount.LabelWithReason".Translate("MooGirl.Mount.CannotMount".Translate(), reasonKey.Translate()).CapitalizeFirst(), rider ?? MooPawn, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            Pawn carrier = MooPawn;
            bool wasSelected = MooGirlSelectionUtility.IsSelectedInPlaying(rider);
            MountedPawnUtility.PreparePawnForMountContainer(rider);
            MountedPawnUtility.BreakRopes(rider);
            MountedPawnUtility.BreakRopes(carrier);
            rider.DeSpawnOrDeselect();

            if (!innerContainer.TryAdd(rider))
            {
                GenSpawn.Spawn(rider, carrier.Position, carrier.Map);
                if (wasSelected)
                {
                    MooGirlSelectionUtility.SelectInPlaying(rider);
                }

                Messages.Message("MooGirl.Mount.LabelWithReason".Translate("MooGirl.Mount.CannotMount".Translate(), "MooGirl.Mount.ReasonInvalid".Translate()).CapitalizeFirst(), carrier, MessageTypeDefOf.RejectInput, historical: false);
                return false;
            }

            MountedPawnUtility.ClearHiddenJobs(rider);
            MountedCombatController.NotifyMounted(this);
            if (wasSelected)
            {
                MooGirlSelectionUtility.SelectInPlaying(rider, playSound: false, forceDesignatorDeselect: false);
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

            bool wasSelected = MooGirlSelectionUtility.IsSelectedInPlaying(rider);
            if (!innerContainer.TryDrop(rider, cell, map, ThingPlaceMode.Near, out Thing dropped, null, c => MountedPawnUtility.DismountCellValidator(c, carrier, rider, map)))
            {
                if (sendMessage)
                {
                    Messages.Message("MooGirl.Mount.NoDismountCell".Translate(carrier.LabelShort), carrier, MessageTypeDefOf.RejectInput, historical: false);
                }

                return false;
            }

            MountedCombatController.NotifyDismounting(this, rider);
            Pawn droppedPawn = dropped as Pawn;
            if (droppedPawn != null && !droppedPawn.Dead && droppedPawn.jobs != null)
            {
                PawnUtility.ForceWait(droppedPawn, 60, carrier);
            }

            if (wasSelected && droppedPawn != null)
            {
                MooGirlSelectionUtility.SelectInPlaying(droppedPawn, playSound: false, forceDesignatorDeselect: false);
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

            Pawn carrier = MooPawn;
            IntVec3 dropCell = cell;
            if (!MountedPawnUtility.DismountCellValidator(dropCell, carrier, rider, map))
            {
                if (carrier == null || !MountedPawnUtility.TryFindDismountCell(carrier, rider, null, out dropCell))
                {
                    return false;
                }
            }

            bool wasSelected = MooGirlSelectionUtility.IsSelectedInPlaying(rider);
            if (!innerContainer.TryDrop(rider, dropCell, map, ThingPlaceMode.Near, out Thing dropped, null, c => MountedPawnUtility.DismountCellValidator(c, carrier, rider, map), playDropSound: false))
            {
                return false;
            }

            MountedCombatController.NotifyDismounting(this, rider);
            Pawn droppedPawn = dropped as Pawn;
            if (droppedPawn != null && !droppedPawn.Dead && droppedPawn.jobs != null)
            {
                PawnUtility.ForceWait(droppedPawn, 60, MooPawn);
            }

            if (wasSelected && droppedPawn != null)
            {
                MooGirlSelectionUtility.SelectInPlaying(droppedPawn, playSound: false, forceDesignatorDeselect: false);
            }

            return true;
        }

        public override void CompTickInterval(int delta)
        {
            base.CompTickInterval(delta);
            Pawn rider = MountedPawn;
            CompProperties_MooGirlMount mountProps = Props;
            if (rider == null || mountProps == null)
            {
                return;
            }

            MooGirlTickUtility.Add(ref physiologicalTickCounter, delta);
            MooGirlTickUtility.Add(ref safetyTickCounter, delta);
            MooGirlTickUtility.Add(ref turretTickCounter, delta);
            MooGirlTickUtility.Add(ref riderMeleeTickCounter, delta);
            MountedCombatController.VerbTick(this, delta);
            MountedCombatController.TickAim(this, delta);

            int physiologicalInterval = Mathf.Max(1, mountProps.physiologicalTickInterval);
            int safetyInterval = Mathf.Max(1, mountProps.safetyCheckInterval);
            int turretInterval = Mathf.Max(1, mountProps.turretTickInterval);

            if (mountProps.tickPhysiology && MooGirlTickUtility.ConsumeReady(ref physiologicalTickCounter, physiologicalInterval, out int physiologicalDelta))
            {
                MountedPawnUtility.MountedPawnTickInterval(rider, physiologicalDelta);
                if (rider.Dead)
                {
                    TryDismount(sendMessage: false);
                    return;
                }
            }

            if (mountProps.autoDismount && MooGirlTickUtility.ConsumeReady(ref safetyTickCounter, safetyInterval, out _))
            {
                if (MountEligibilityService.ShouldAutoDismount(rider, MooPawn, out string reasonKey))
                {
                    string reason = reasonKey.NullOrEmpty() ? "MooGirl.Mount.ReasonInvalid".Translate().ToString() : reasonKey.Translate().ToString();
                    if (TryDismount(sendMessage: false))
                    {
                        Messages.Message("MooGirl.Mount.AutoDismountMessage".Translate(rider.LabelShort, reason), MooPawn, MessageTypeDefOf.NeutralEvent);
                    }

                    return;
                }
            }

            if (MooGirlTickUtility.ConsumeReady(ref turretTickCounter, turretInterval, out int turretDelta))
            {
                MountedCombatController.Tick(this, turretDelta);
            }

            if (MooGirlTickUtility.ConsumeReady(ref riderMeleeTickCounter, turretInterval, out _))
            {
                MountedPawnMeleeSupport.Tick(this);
            }
        }

    }
}
