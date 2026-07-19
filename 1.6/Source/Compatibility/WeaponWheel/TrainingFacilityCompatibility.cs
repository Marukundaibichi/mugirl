using Verse;

namespace Mugirl
{
    internal static class TrainingFacilityCompatibility
    {
        private const string PackageId = "Haplo.Miscellaneous.Training";

        internal static bool Active => ModLister.GetActiveModWithIdentifier(PackageId) != null;

        internal static bool ShouldBypassWeaponWheelCombat(Pawn pawn)
        {
            if (!Active)
            {
                return false;
            }

            string jobDefName = pawn?.CurJobDef?.defName;
            return jobDefName == "UseShootingRange"
                || jobDefName == "UseShootingRange_NonJoy"
                || jobDefName == "UseShootingRange_NonJoy_Work";
        }
    }
}
