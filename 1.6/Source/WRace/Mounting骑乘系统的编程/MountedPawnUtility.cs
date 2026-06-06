using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public static class MountedPawnUtility
    {
        public static bool IsMooGirl(Pawn pawn)
        {
            return MooGirlIdentity.IsMooGirlPawn(pawn);
        }

        public static Comp_MooGirlMount GetMountComp(Pawn pawn)
        {
            return pawn?.TryGetComp<Comp_MooGirlMount>();
        }

        public static Comp_MooGirlMount GetMountForRider(Pawn rider)
        {
            if (rider == null)
            {
                return null;
            }

            return rider.ParentHolder as Comp_MooGirlMount ?? ThingOwnerUtility.GetAnyParent<Comp_MooGirlMount>(rider);
        }

        public static bool IsMounted(Pawn pawn, out Comp_MooGirlMount comp)
        {
            comp = GetMountForRider(pawn);
            return comp != null && comp.MountedPawn == pawn;
        }

        public static bool HasAnyRope(Pawn pawn)
        {
            return pawn?.roping?.HasAnyRope == true;
        }

        public static void BreakRopes(Pawn pawn)
        {
            pawn?.roping?.BreakAllRopes();
        }

        public static bool TryFindDismountCell(Pawn carrier, Pawn rider, IntVec3? preferredCell, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            Map map = carrier?.Map;
            if (carrier == null || map == null)
            {
                return false;
            }

            if (preferredCell.HasValue && DismountCellValidator(preferredCell.Value, carrier, rider, map))
            {
                cell = preferredCell.Value;
                return true;
            }

            foreach (IntVec3 c in GenAdj.CellsAdjacent8Way(carrier))
            {
                if (DismountCellValidator(c, carrier, rider, map))
                {
                    cell = c;
                    return true;
                }
            }

            for (int radius = 2; radius <= 5; radius++)
            {
                if (CellFinder.TryFindRandomCellNear(carrier.Position, map, radius, c => DismountCellValidator(c, carrier, rider, map), out cell))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool DismountCellValidator(IntVec3 c, Pawn carrier, Pawn rider, Map map)
        {
            if (!c.InBounds(map) || c.Fogged(map) || !c.Standable(map))
            {
                return false;
            }

            if (c.GetFirstPawn(map) != null)
            {
                return false;
            }

            if (carrier != null && !GenSight.LineOfSight(carrier.Position, c, map, skipFirstCell: true))
            {
                return false;
            }

            return rider == null || GenPlace.HaulPlaceBlockerIn(rider, c, map, checkBlueprintsAndFrames: false) == null;
        }

        public static void PreparePawnForMountContainer(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            pawn.jobs?.ClearQueuedJobs();
            if (pawn.jobs?.curJob != null)
            {
                pawn.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }

            pawn.pather?.StopDead();
            pawn.stances?.CancelBusyStanceHard();
            if (pawn.drafter != null)
            {
                pawn.drafter.Drafted = false;
                pawn.drafter.FireAtWill = false;
            }

            pawn.mindState?.priorityWork.ClearPrioritizedWorkAndJobQueue();
        }

        public static void PhysiologyTick(Pawn rider, int delta)
        {
            if (rider == null || rider.Destroyed)
            {
                return;
            }

            rider.health?.HealthTickInterval(delta);
            if (rider.Dead)
            {
                return;
            }

            rider.needs?.NeedsTrackerTickInterval(delta);
            rider.ageTracker?.AgeTickInterval(delta);
            rider.apparel?.ApparelTrackerTickInterval(delta);
            rider.skills?.SkillsTickInterval(delta);
            rider.genes?.GeneTrackerTickInterval(delta);

            if (!rider.Spawned)
            {
                ClearHiddenJobs(rider);
            }
        }

        public static void ClearHiddenJobs(Pawn rider)
        {
            rider.jobs?.ClearQueuedJobs();
            if (rider.jobs?.curJob != null)
            {
                rider.jobs.EndCurrentJob(JobCondition.InterruptForced, startNewJob: false);
            }

            rider.pather?.StopDead();
            rider.stances?.CancelBusyStanceHard();
        }

        public static bool ShouldAutoDismount(Pawn rider, Pawn carrier, out string reasonKey)
        {
            reasonKey = null;
            if (rider == null || carrier == null)
            {
                reasonKey = "MooGirl.Mount.ReasonInvalid";
                return true;
            }

            if (!carrier.Spawned || carrier.Destroyed || carrier.Dead || carrier.Downed || carrier.IsBurning() || carrier.InMentalState)
            {
                reasonKey = "MooGirl.Mount.ReasonTargetBadState";
                return true;
            }

            if (rider.Destroyed || rider.Dead || rider.Downed || rider.IsBurning() || rider.InMentalState)
            {
                reasonKey = "MooGirl.Mount.ReasonRiderBadState";
                return true;
            }

            Need_Food food = rider.needs?.food;
            if (food != null && (food.Starving || food.CurLevelPercentage <= food.PercentageThreshUrgentlyHungry))
            {
                reasonKey = "MooGirl.Mount.ReasonRiderHungry";
                return true;
            }

            Need_Rest rest = rider.needs?.rest;
            if (rest != null && rest.CurLevel < Need_Rest.ThreshVeryTired)
            {
                reasonKey = "MooGirl.Mount.ReasonRiderTired";
                return true;
            }

            if (HealthAIUtility.ShouldSeekMedicalRest(rider))
            {
                reasonKey = "MooGirl.Mount.ReasonRiderMedical";
                return true;
            }

            if (HasAnyRope(rider) || HasAnyRope(carrier))
            {
                reasonKey = "MooGirl.Mount.ReasonRoped";
                return true;
            }

            return false;
        }

        public static IEnumerable<Gizmo> GetMountedPawnGizmos(Pawn rider, Comp_MooGirlMount comp)
        {
            Pawn carrier = comp?.MooPawn;
            if (carrier != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "MooGirl.Mount.SelectCarrier".Translate(),
                    defaultDesc = "MooGirl.Mount.SelectCarrierDesc".Translate(),
                    icon = TexCommand.SelectCarriedThing,
                    action = delegate
                    {
                        Find.Selector.ClearSelection();
                        Find.Selector.Select(carrier);
                    }
                };
            }

            yield return new Command_Action
            {
                defaultLabel = "MooGirl.Mount.DismountRider".Translate(),
                defaultDesc = "MooGirl.Mount.DismountRiderDesc".Translate(),
                icon = TexCommand.DropCarriedPawn,
                action = delegate
                {
                    comp?.TryDismount();
                }
            };

            yield break;
        }

        public static Vector3 OffsetForRot(CompProperties_MooGirlMount props, Rot4 rotation)
        {
            switch (rotation.AsInt)
            {
                case 0:
                    return props.northOffset;
                case 1:
                    return props.eastOffset;
                case 2:
                    return props.southOffset;
                case 3:
                    return props.westOffset;
                default:
                    return props.southOffset;
            }
        }
    }
}
