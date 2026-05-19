using RimWorld;
using System.Linq;
using Verse;

namespace MooGirl
{
    public class ThoughtWorker_SingleshotWeaponAversion : ThoughtWorker
    {
        protected override ThoughtState CurrentStateInternal(Pawn p)
        {
            var weaponDef = p.equipment?.Primary?.def;
            return weaponDef?.Verbs?.Any(v => v.burstShotCount == 1) ?? false;
        }
    }
}
