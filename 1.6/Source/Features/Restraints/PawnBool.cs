using Verse;

namespace MooGirl
{
    public static class PawnBool
    {
        public static bool SimpleSlaveryIsActive;
        public static HediffDef Enslaved;
        public static string get_pawnname(Pawn who)
        {

            if (who == null) return "null";

            string name = who.Label;
            if (name != null)
            {
                if (who.Name?.ToStringShort != null)
                    name = who.Name.ToStringShort;
            }
            else
                name = "noname";

            return name;
        }

        public static bool is_slave(Pawn pawn)
        {
            if (is_vanillaslave(pawn) || is_modslave(pawn))
                return true;
            else
                return false;
        }
        public static bool is_vanillaslave(Pawn pawn)
        {
            if (pawn.IsSlave)
                return true;
            else
                return false;
        }
        public static bool is_modslave(Pawn pawn)
        {
            if (SimpleSlaveryIsActive)
                return pawn?.health.hediffSet.HasHediff(PawnBool.Enslaved) ?? false;
            else
                return false;
        }
    }
}
