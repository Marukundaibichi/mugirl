using Verse;

namespace Mugirl
{
    public static class PawnGenerator_GeneratePawn_Patch
    {
        public static void Postfix(Pawn __result)
        {
            __result.EnsureWornSlaveApparelLocks();
        }
    }
}
