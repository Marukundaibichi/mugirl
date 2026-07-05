using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public static class RopingService
    {
        public static bool IsMugirlRopee(Pawn pawn)
        {
            // 牵引规则保留历史边界：按 Mugirl body 识别 ropee，允许 HAR 变体共用该身体。
            return MugirlIdentity.HasMugirlBody(pawn);
        }

        public static bool CanStartPawnRope(Pawn roper, Pawn ropee)
        {
            if (roper == null || ropee == null || roper == ropee)
            {
                return false;
            }

            if (!IsMugirlRopee(ropee))
            {
                return false;
            }

            if (!roper.Spawned || !ropee.Spawned || roper.Dead || ropee.Dead || roper.Destroyed || ropee.Destroyed || roper.Map != ropee.Map)
            {
                return false;
            }

            if (roper.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true ||
                ropee.health?.capacities?.CapableOf(PawnCapacityDefOf.Moving) != true)
            {
                return false;
            }

            if (IsFollowingRoper(roper))
            {
                return false;
            }

            bool ropeeFollowingRoper = IsFollowingRoper(ropee);
            bool ropeeRopedToSpot = IsRopedToSpot(ropee);
            if (ropeeFollowingRoper && !ropeeRopedToSpot)
            {
                return false;
            }

            if (ropee.stances?.stunner?.Stunned == true)
            {
                return false;
            }

            if (IsMugirlRopee(roper) && IsMugirlRopee(ropee))
            {
                return false;
            }

            if (ropee.InMentalState && ropee.MentalState is MentalState_Berserk)
            {
                return false;
            }

            return true;
        }

        public static bool IsFollowingRoper(Pawn pawn)
        {
            return pawn?.CurJob?.def == Mugirl_DefOf.Job_FollowRoper;
        }

        public static bool IsRopedByPawn(Pawn pawn)
        {
            if (pawn?.roping?.IsRopedByPawn == true)
            {
                return true;
            }

            MapRopingIndex index = IndexFor(pawn);
            return index?.RoperFor(pawn) != null;
        }

        public static Pawn RoperFor(Pawn ropee)
        {
            Pawn roper = ropee?.roping?.RopedByPawn;
            if (roper != null)
            {
                return roper;
            }

            MapRopingIndex index = IndexFor(ropee);
            return index?.RoperFor(ropee);
        }

        public static bool IsRopedToSpot(Pawn pawn)
        {
            if (pawn?.roping?.IsRopedToSpot == true)
            {
                return true;
            }

            MapRopingIndex index = IndexFor(pawn);
            return index != null && index.IsRopedToSpot(pawn);
        }

        public static bool HasAnyRope(Pawn pawn)
        {
            return pawn?.roping?.HasAnyRope == true || IsPendingSpotRope(pawn);
        }

        public static bool IsBlockedFromEscape(Pawn pawn)
        {
            return IsRopedToSpot(pawn) || IsPendingSpotRope(pawn);
        }

        public static void RegisterPawnRope(Pawn roper, Pawn ropee)
        {
            if (roper?.roping == null || ropee == null)
            {
                return;
            }

            MapRopingIndex index = IndexFor(roper);
            if (index != null)
            {
                index.RegisterPawnRope(roper, ropee);
            }
        }

        public static void RegisterRopedToSpot(Pawn ropee)
        {
            MapRopingIndex index = IndexFor(ropee);
            if (index != null)
            {
                index.RegisterRopedToSpot(ropee);
            }
        }

        public static void NotifyBreakAllRopes(Pawn pawn)
        {
            MapRopingIndex index = IndexFor(pawn);
            if (index != null)
            {
                index.RemovePawn(pawn);
            }
        }

        public static void NotifyPawnNoLongerRopedToTarget(Pawn pawn)
        {
            MapRopingIndex index = IndexFor(pawn);
            if (index != null)
            {
                index.RemovePawnAsRopee(pawn);
            }
        }

        public static void BreakAllRopesAndNotify(Pawn pawn)
        {
            if (pawn == null)
            {
                return;
            }

            pawn.roping?.BreakAllRopes();
            NotifyBreakAllRopes(pawn);
        }

        public static void MarkPendingSpotRope(Pawn pawn)
        {
            MapRopingIndex index = IndexFor(pawn);
            if (index != null)
            {
                index.MarkPendingSpotRope(pawn);
            }
        }

        public static void ClearPendingSpotRope(Pawn pawn)
        {
            MapRopingIndex index = IndexFor(pawn);
            if (index != null)
            {
                index.ClearPendingSpotRope(pawn);
            }
        }

        public static void ClearPendingSpotRope(Pawn pawn, Map map)
        {
            MapRopingIndex index = map?.GetComponent<MapRopingIndex>();
            if (index != null)
            {
                index.ClearPendingSpotRope(pawn);
            }

            if (pawn?.Map != null && pawn.Map != map)
            {
                ClearPendingSpotRope(pawn);
            }
        }

        public static void NotifyWallRopeHitchRemoved(Map map, IntVec3 cell)
        {
            if (map?.mapPawns?.AllPawnsSpawned == null || !cell.IsValid)
            {
                return;
            }

            IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn pawn = pawns[i];
                Pawn_RopeTracker tracker = pawn?.roping;
                if (!IsMugirlRopee(pawn) ||
                    tracker?.IsRopedToSpot != true ||
                    tracker.RopedTo.Cell != cell)
                {
                    continue;
                }

                tracker.UnropeFromSpot();
                NotifyPawnNoLongerRopedToTarget(pawn);
            }
        }

        public static bool IsPendingSpotRope(Pawn pawn)
        {
            MapRopingIndex index = IndexFor(pawn);
            return index != null && index.IsPendingSpotRope(pawn);
        }

        public static Pawn FirstMugirlFollowing(Pawn roper)
        {
            List<Pawn> ropees = RopeesFor(roper);
            if (ropees == null)
            {
                return null;
            }

            for (int i = 0; i < ropees.Count; i++)
            {
                Pawn ropee = ropees[i];
                if (IsMugirlRopee(ropee) && IsFollowingRoper(ropee) && ropee.CurJob.targetA.Thing == roper)
                {
                    return ropee;
                }
            }

            return null;
        }

        public static int CountMugirlFollowers(Pawn roper, int stopAt)
        {
            List<Pawn> ropees = RopeesFor(roper);
            if (ropees == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < ropees.Count; i++)
            {
                Pawn ropee = ropees[i];
                if (!IsMugirlRopee(ropee) || !IsFollowingRoper(ropee) || ropee.CurJob.targetA.Thing != roper)
                {
                    continue;
                }

                count++;
                if (count >= stopAt)
                {
                    break;
                }
            }

            return count;
        }

        public static bool HasOnlyMugirlRopees(Pawn_RopeTracker tracker)
        {
            if (tracker == null || tracker.Ropees == null || tracker.Ropees.Count == 0)
            {
                return false;
            }

            List<Pawn> ropees = tracker.Ropees;
            for (int i = 0; i < ropees.Count; i++)
            {
                if (!IsMugirlRopee(ropees[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public static void RefreshRoperFromTracker(Pawn roper)
        {
            if (roper?.roping?.Ropees == null)
            {
                return;
            }

            MapRopingIndex index = IndexFor(roper);
            if (index == null)
            {
                return;
            }

            List<Pawn> ropees = roper.roping.Ropees;
            for (int i = 0; i < ropees.Count; i++)
            {
                index.RegisterPawnRope(roper, ropees[i]);
            }
        }

        private static List<Pawn> RopeesFor(Pawn roper)
        {
            MapRopingIndex index = IndexFor(roper);
            List<Pawn> ropees = index?.RopeesFor(roper);
            if (ropees != null && ropees.Count > 0)
            {
                return ropees;
            }

            RefreshRoperFromTracker(roper);
            return index?.RopeesFor(roper);
        }

        private static MapRopingIndex IndexFor(Pawn pawn)
        {
            return pawn?.Map?.GetComponent<MapRopingIndex>();
        }
    }
}
