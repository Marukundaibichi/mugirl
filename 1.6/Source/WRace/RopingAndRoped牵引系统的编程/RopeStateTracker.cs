using System.Collections.Generic;
using Verse;

namespace MooGirl
{
    public static class RopeStateTracker
    {
        public static HashSet<Pawn> PendingRope = new HashSet<Pawn>();

        public static void MarkPendingRope(Pawn pawn)
        {
            if (pawn != null && !PendingRope.Contains(pawn))
                PendingRope.Add(pawn);
        }

        public static void ClearPendingRope(Pawn pawn)
        {
            if (pawn != null)
                PendingRope.Remove(pawn);
        }

        public static bool IsPendingRope(Pawn pawn)
        {
            return pawn != null && PendingRope.Contains(pawn);
        }
    }
}
