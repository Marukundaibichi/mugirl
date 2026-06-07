using Verse;

namespace MooGirl
{
    internal static class PawnSlaveStatusUtility
    {
        internal static string DisplayName(Pawn pawn)
        {
            if (pawn == null)
            {
                return "null";
            }

            return pawn.Name?.ToStringShort ?? pawn.Label ?? "noname";
        }

        internal static bool IsSlave(Pawn pawn)
        {
            return pawn?.IsSlave == true;
        }
    }
}
