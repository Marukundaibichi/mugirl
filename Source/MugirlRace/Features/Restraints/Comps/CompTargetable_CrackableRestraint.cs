namespace Mugirl.Features.Restraints;

// 解码器的目标选择：只能选地面上未破解的高级束具。
public class CompTargetable_CrackableRestraint : CompTargetable
{
    // 原版 targetParams 属性每次访问都调用 GetTargetingParameters；校验器无状态，因此每个 comp 只创建一次。
    private TargetingParameters? targetingParameters;

    protected override bool PlayerChoosesTarget => true;

    protected override TargetingParameters GetTargetingParameters()
    {
        return targetingParameters ??= new TargetingParameters
        {
            canTargetPawns = false,
            canTargetBuildings = false,
            canTargetAnimals = false,
            canTargetMechs = false,
            canTargetItems = true,
            mapObjectTargetsMustBeAutoAttackable = false,
            validator = static target => target.Thing is Apparel apparel && IsCrackableGroundApparel(apparel),
        };
    }

    public override IEnumerable<Thing> GetTargets(Thing? targetChosenByPlayer = null)
    {
        if (targetChosenByPlayer != null && ValidateTarget(targetChosenByPlayer, false))
        {
            yield return targetChosenByPlayer;
        }
    }

    public override bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
    {
        if (target.Thing is not Apparel apparel || !apparel.Spawned || !apparel.IsAdvancedRestraint())
        {
            if (showMessages)
            {
                Messages.Message("Mugirl.InvalidTarget".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
            }

            return false;
        }

        if (apparel.IsCrackedAdvancedRestraint())
        {
            if (showMessages)
            {
                Messages.Message("Mugirl.AlreadyCracked".Translate(), MessageTypeDefOf.NeutralEvent, historical: false);
            }

            return false;
        }

        return base.ValidateTarget(target, showMessages);
    }

    private static bool IsCrackableGroundApparel(Apparel apparel)
    {
        return apparel.Spawned && apparel.IsAdvancedRestraint() && !apparel.IsCrackedAdvancedRestraint();
    }
}
