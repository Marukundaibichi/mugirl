using RimWorld;
using Verse;

namespace Mugirl
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
                Messages.Message("Mugirl.CrackBondageGear_TargetNotValid".Translate(), MessageTarget(user, target), MessageTypeDefOf.RejectInput, historical: false);
            }
        }

        private void TryCrackGroundApparel(Apparel apparel, Pawn user)
        {
            if (apparel is BrainWashSlaveApparel brain)
            {
                Messages.Message("Mugirl.CrackBondageGear_CrackBrainwash".Translate(brain.Label), brain, MessageTypeDefOf.PositiveEvent, historical: false);
                brain.Crack();
            }
            else if (apparel is AdvancedSlaveApparel adv)
            {
                Messages.Message("Mugirl.CrackBondageGear_CrackAdvanced".Translate(adv.Label), adv, MessageTypeDefOf.PositiveEvent, historical: false);
                adv.Crack();
            }
            else
            {
                Messages.Message("Mugirl.CrackBondageGear_NoCrackableType".Translate(), MessageTarget(user, apparel), MessageTypeDefOf.RejectInput, historical: false);
                MugirlLog.WarningOnce("CrackBondageGear.NoCrackableType", "Mugirl.CrackBondageGear_NoCrackableType".Translate().ToString());
            }
        }

        private static Thing MessageTarget(Pawn user, Thing fallback)
        {
            return user ?? fallback;
        }
    }

}
