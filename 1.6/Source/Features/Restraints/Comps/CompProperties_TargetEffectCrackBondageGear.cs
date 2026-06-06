using RimWorld;
using Verse;

namespace MooGirl
{
    public class CompProperties_TargetEffectCrackBondageGear : CompProperties
    {
        public CompProperties_TargetEffectCrackBondageGear()
        {
            this.compClass = typeof(CompTargetCrackBondageGear);
        }
    }

    public class CompTargetCrackBondageGear : CompTargetEffect
    {
        public override void DoEffectOn(Pawn user, Thing target)
        {
            if (target is Apparel apparel && apparel.IsAdvancedApparel() && !apparel.IsUnlockAdvancedApparel())
            {
                TryCrackGroundApparel(apparel, user);
            }
            else
            {
                Log.Warning("MooGirl.CrackBondageGear_TargetNotValid".Translate());
            }
        }

        private void TryCrackGroundApparel(Apparel apparel, Pawn user)
        {
            if (apparel is AdvancedSlaveApparel adv)
            {
                Log.Message("MooGirl.CrackBondageGear_CrackAdvanced".Translate(adv.Label));
                adv.Crack();
            }
            else if (apparel is BrainWashSlaveApparel brain)
            {
                Log.Message("MooGirl.CrackBondageGear_CrackBrainwash".Translate(brain.Label));
                brain.Crack();
            }
            else
            {
                Log.Warning("MooGirl.CrackBondageGear_NoCrackableType".Translate());
            }
        }
    }

}
