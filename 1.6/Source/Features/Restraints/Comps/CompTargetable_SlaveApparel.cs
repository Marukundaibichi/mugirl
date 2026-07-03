using RimWorld;
using System.Collections.Generic;
using Verse;

namespace Mugirl
{
    public class CompTargetable_SlaveApparel : CompTargetable
    {
        protected override bool PlayerChoosesTarget
        {
            get
            {
                return true;
            }
        }

        // 目标选择参数
        protected override TargetingParameters GetTargetingParameters()
        {
            return new TargetingParameters
            {
                canTargetPawns = false,
                canTargetBuildings = false,
                canTargetAnimals = false,
                canTargetMechs = false,
                canTargetItems = true,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = delegate (TargetInfo target)
                {
                    if (!target.HasThing)
                    {
                        return false;
                    }

                    return target.Thing is Apparel apparel && apparel.Spawned && apparel.IsAdvancedApparel() && !apparel.IsUnlockAdvancedApparel();
                }
            };
        }

        public override IEnumerable<Thing> GetTargets(Thing targetChosenByPlayer = null)
        {
            if (targetChosenByPlayer != null && ValidateTarget(targetChosenByPlayer, false))
            {
                yield return targetChosenByPlayer;
            }
        }

        public override bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
        {
            if (target.HasThing && target.Thing is Apparel apparel && apparel.Spawned && apparel.IsAdvancedApparel())
            {
                if (apparel is AdvancedSlaveApparel slaveApparel && slaveApparel.IsCracked())
                {
                    if (showMessages)
                    {
                        Messages.Message("Mugirl.AlreadyCracked".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
                    }
                    return false;
                }
                return base.ValidateTarget(target, showMessages);
            }
            else
            {
                if (showMessages)
                {
                    Messages.Message("Mugirl.InvalidTarget".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
                }
                return false;
            }
        }

    }
}
