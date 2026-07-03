using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public static class MountedPawnUtility
    {
        public static bool IsMugirl(Pawn pawn)
        {
            return MugirlIdentity.IsMugirlPawn(pawn);
        }

        public static Comp_MugirlMount GetMountComp(Pawn pawn)
        {
            return pawn?.TryGetComp<Comp_MugirlMount>();
        }

        public static Comp_MugirlMount GetMountForRider(Pawn rider)
        {
            if (rider == null)
            {
                return null;
            }

            return rider.ParentHolder as Comp_MugirlMount ?? ThingOwnerUtility.GetAnyParent<Comp_MugirlMount>(rider);
        }

        public static bool IsMounted(Pawn pawn, out Comp_MugirlMount comp)
        {
            comp = GetMountForRider(pawn);
            return comp != null && comp.MountedPawn == pawn;
        }

        public static bool HasAnyRope(Pawn pawn)
        {
            return RopingService.HasAnyRope(pawn);
        }

        public static bool IsHumanlike(Pawn pawn)
        {
            return pawn?.RaceProps?.Humanlike == true;
        }

        public static bool HasCapacity(Pawn pawn, PawnCapacityDef capacity)
        {
            return pawn?.health?.capacities?.CapableOf(capacity) == true;
        }

        public static bool IsAwake(Pawn pawn)
        {
            if (pawn?.health?.capacities?.CanBeAwake != true)
            {
                return false;
            }

            Pawn_JobTracker jobs = pawn.jobs;
            if (jobs?.curJob != null && jobs.curDriver != null)
            {
                return !jobs.curDriver.asleep;
            }

            return true;
        }

        public static Verb TryGetMeleeVerb(Pawn pawn, Thing target)
        {
            return pawn?.meleeVerbs?.TryGetMeleeVerb(target);
        }

        public static BodyPartRecord GetRandomNotMissingPart(Pawn pawn, DamageDef damageDef, BodyPartHeight height, BodyPartDepth depth)
        {
            return pawn?.health?.hediffSet?.GetRandomNotMissingPart(damageDef, height, depth);
        }

        public static float EquipmentDrawDistanceFactor(Pawn pawn)
        {
            return pawn?.ageTracker?.CurLifeStage?.equipmentDrawDistanceFactor ?? 1f;
        }

        public static Vector3 MountedWeaponSideOffset(Rot4 rotation, float distance)
        {
            if (rotation == Rot4.East || rotation == Rot4.West)
            {
                return Vector3.zero;
            }

            return rotation.RighthandCell.ToVector3() * distance;
        }

        public static void BreakRopes(Pawn pawn)
        {
            RopingService.BreakAllRopesAndNotify(pawn);
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

        public static void MountedPawnTickInterval(Pawn rider, int delta)
        {
            if (rider == null || rider.Destroyed)
            {
                return;
            }

            bool suspended = rider.Suspended;
            if (suspended)
            {
                rider.guilt?.GuiltTrackerTickInterval(delta);
                return;
            }

            // 骑手在 ThingOwner 容器内没有地图位置，不能让 job/寻路/社交等生成态逻辑自行推进；
            // 这里仅同步原版 interval 中与长期生存、DLC 环境、关系和记录有关的 tracker。
            rider.health?.HealthTickInterval(delta);
            if (rider.Dead)
            {
                return;
            }

            rider.mindState?.MindStateTickInterval(delta);
            rider.carryTracker?.CarryHandsTickInterval(delta);
            if (!rider.InCryptosleep && IsHumanlike(rider))
            {
                rider.infectionVectors?.InfectionTickInterval(delta);
            }

            Thing firstParentThing = ThingOwnerUtility.GetFirstParentThing(rider);
            if (!rider.Spawned && firstParentThing != null)
            {
                PawnUtility.GainComfortFromThingIfPossible(rider, firstParentThing, delta);
            }

            rider.needs?.NeedsTrackerTickInterval(delta);
            rider.apparel?.ApparelTrackerTickInterval(delta);
            rider.caller?.CallTrackerTickInterval(delta);
            rider.skills?.SkillsTickInterval(delta);
            rider.drafter?.DraftControllerTickInterval(delta);
            rider.relations?.RelationsTrackerTickInterval(delta);

            if (ModsConfig.RoyaltyActive && rider.psychicEntropy != null)
            {
                rider.psychicEntropy.PsychicEntropyTrackerTickInterval(delta);
            }

            if (IsHumanlike(rider))
            {
                rider.guest?.GuestTrackerTickInterval(delta);
            }

            rider.ideo?.IdeoTrackerTickInterval(delta);
            rider.genes?.GeneTrackerTickInterval(delta);

            if (ModsConfig.RoyaltyActive && rider.royalty != null)
            {
                rider.royalty.RoyaltyTrackerTickInterval(delta);
            }

            if (ModsConfig.IdeologyActive)
            {
                rider.style?.StyleTrackerTickInterval(delta);
                rider.styleObserver?.StyleObserverTickInterval(delta);
                rider.surroundings?.SurroundingsTrackerTickInterval(delta);
            }

            if (ModsConfig.BiotechActive)
            {
                rider.learning?.LearningTickInterval(delta);
                PollutionUtility.PawnPollutionTickInterval(rider, delta);
            }

            GasUtility.PawnGasEffectsTickInterval(rider, delta);
            ToxicUtility.PawnToxicTickInterval(rider, delta);
            VacuumUtility.PawnVacuumTickInterval(rider, delta);

            if (!rider.IsMutant || rider.mutant?.Def?.disableAging != true)
            {
                rider.ageTracker?.AgeTickInterval(delta);
            }

            rider.records?.RecordsTickInterval(delta);
            rider.guilt?.GuiltTrackerTickInterval(delta);

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
            return MountEligibilityService.ShouldAutoDismount(rider, carrier, out reasonKey);
        }

        public static IEnumerable<Gizmo> GetMountedPawnGizmos(Pawn rider, Comp_MugirlMount comp)
        {
            Pawn carrier = comp?.MooPawn;
            if (carrier != null)
            {
                yield return new Command_Action
                {
                    defaultLabel = "Mugirl.Mount.SelectCarrier".Translate(),
                    defaultDesc = "Mugirl.Mount.SelectCarrierDesc".Translate(),
                    icon = TexCommand.SelectCarriedThing,
                    action = delegate
                    {
                        Comp_MugirlMount currentComp = GetMountForRider(rider) ?? comp;
                        Pawn currentCarrier = currentComp?.MooPawn;
                        if (currentCarrier == null)
                        {
                            return;
                        }

                        MugirlSelectionUtility.SelectInPlaying(currentCarrier);
                    }
                };
            }

            yield return new Command_Action
            {
                defaultLabel = "Mugirl.Mount.DismountRider".Translate(),
                defaultDesc = "Mugirl.Mount.DismountRiderDesc".Translate(),
                icon = TexCommand.DropCarriedPawn,
                action = delegate
                {
                    Comp_MugirlMount currentComp = GetMountForRider(rider) ?? comp;
                    currentComp?.TryDismount();
                }
            };

            yield break;
        }

        public static Vector3 OffsetForRot(CompProperties_MugirlMount props, Rot4 rotation)
        {
            if (props == null)
            {
                return Vector3.zero;
            }

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
