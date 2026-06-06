using RimWorld;
using System.Collections.Generic;
using Verse;

namespace MooGirl
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
                        return false;

                    // 只接受 Apparel 且是在地上
                    if (target.Thing is Apparel apparel && apparel.ParentHolder is Map)
                    {
                        return true;
                    }

                    return false;
                }
            };
        }

        // 获取玩家选择的目标
        public override IEnumerable<Thing> GetTargets(Thing targetChosenByPlayer = null)
        {
            yield return targetChosenByPlayer;
            yield break;
        }

        // 校验目标是否有效
        public override bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
        {
            if (target.Thing is Apparel apparel && apparel.IsAdvancedApparel())
            {
                if (apparel is AdvancedSlaveApparel slaveApparel && slaveApparel.IsCracked())
                {
                    if (showMessages)
                    {
                        Messages.Message("MooGirl.AlreadyCracked".Translate(), MessageTypeDefOf.NeutralEvent);
                    }
                    return false;
                }
                return base.ValidateTarget(target, showMessages);
            }
            else
            {
                if (showMessages)
                {
                    Messages.Message("MooGirl.InvalidTarget".Translate(), MessageTypeDefOf.NeutralEvent);
                }
                return false;
            }
        }

    }
}
