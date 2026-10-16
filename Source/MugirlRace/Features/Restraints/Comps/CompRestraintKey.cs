namespace Mugirl.Features.Restraints;

public class CompRestraintKey : ThingComp
{
    public override bool AllowStackWith(Thing other)
    {
        return false;
    }
}
