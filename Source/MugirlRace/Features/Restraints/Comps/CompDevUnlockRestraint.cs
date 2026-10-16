namespace Mugirl.Features.Restraints;

public class CompDevUnlockRestraint : ThingComp
{
    public override IEnumerable<Gizmo> CompGetWornGizmosExtra()
    {
        foreach (Gizmo gizmo in base.CompGetWornGizmosExtra())
        {
            yield return gizmo;
        }

        if (!Prefs.DevMode || parent is not RestraintApparel apparel || apparel.Wearer == null)
        {
            yield break;
        }

        yield return new Command_Action
        {
            defaultLabel = "Mugirl.Restraints.DevUnlock.Label".Translate(apparel.LabelCap),
            defaultDesc = "Mugirl.Restraints.DevUnlock.Desc".Translate(apparel.LabelCap),
            icon = TexCommand.ClearPrioritizedWork,
            action = apparel.DevUnlock,
        };
    }
}
