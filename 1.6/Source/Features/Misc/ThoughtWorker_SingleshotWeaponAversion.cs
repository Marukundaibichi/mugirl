using RimWorld;
using Verse;

namespace Mugirl
{
    public class ThoughtWorker_SingleshotWeaponAversion : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            if (!MugirlIdentity.IsMugirlPawn(p))
            {
                return false;
            }

            ThingDef weaponDef = p.equipment?.Primary?.def;
            if (weaponDef?.Verbs == null)
            {
                return false;
            }

            for (int i = 0; i < weaponDef.Verbs.Count; i++)
            {
                if (weaponDef.Verbs[i].burstShotCount == 1)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
