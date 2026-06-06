using System.Collections.Generic;
using RimWorld;
using Verse;

namespace MooGirl
{
    public static class RopingService
    {
        public static bool IsMooGirlRopee(Pawn pawn)
        {
            // 本阶段先保留旧行为：仍以 MooGirlBody 判定，避免影响异种框架变体兼容边界。
            return pawn?.RaceProps?.body == MooGirl_DefOf.MooGirlBody;
        }

        public static bool IsFollowingRoper(Pawn pawn)
        {
            return pawn?.CurJob?.def == MooGirl_DefOf.Job_FollowRoper;
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

        public static bool IsPendingSpotRope(Pawn pawn)
        {
            MapRopingIndex index = IndexFor(pawn);
            return index != null && index.IsPendingSpotRope(pawn);
        }

        public static Pawn FirstMooGirlFollowing(Pawn roper)
        {
            List<Pawn> ropees = RopeesFor(roper);
            if (ropees == null)
            {
                return null;
            }

            for (int i = 0; i < ropees.Count; i++)
            {
                Pawn ropee = ropees[i];
                if (IsMooGirlRopee(ropee) && IsFollowingRoper(ropee) && ropee.CurJob.targetA.Thing == roper)
                {
                    return ropee;
                }
            }

            return null;
        }

        public static int CountMooGirlFollowers(Pawn roper, int stopAt)
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
                if (!IsMooGirlRopee(ropee) || !IsFollowingRoper(ropee) || ropee.CurJob.targetA.Thing != roper)
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

        public static bool HasOnlyMooGirlRopees(Pawn_RopeTracker tracker)
        {
            if (tracker == null || tracker.Ropees == null || tracker.Ropees.Count == 0)
            {
                return false;
            }

            List<Pawn> ropees = tracker.Ropees;
            for (int i = 0; i < ropees.Count; i++)
            {
                if (!IsMooGirlRopee(ropees[i]))
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
