namespace Mugirl.Features.Restraints;

// 佩戴已破解的洗脑头盔达到指定时长后，对囚犯削减意志或直接转为奴隶（各一次）。
public class HediffComp_ReduceWillOrEnslave : HediffComp_DelayedTrigger
{
    public HediffCompProperties_ReduceWillOrEnslave? Props => props as HediffCompProperties_ReduceWillOrEnslave;

    protected override int? ConfiguredTriggerTicks => Props?.triggerTicks;

    protected override string AgeScribeLabel => "mugirlReduceWillAge";

    protected override string TriggeredScribeLabel => "mugirlReduceWillTriggered";

    // 两项都未配置时直接视为已处理；配置了的操作至少一项成功才算处理（同 1.6）。
    protected override bool TryTrigger(Pawn pawn)
    {
        HediffCompProperties_ReduceWillOrEnslave? compProps = Props;
        if (compProps == null || !pawn.IsPrisoner || !pawn.IsWearingCrackedBrainwashApparel())
        {
            return false;
        }

        if (!compProps.reduceWill && !compProps.makeSlave)
        {
            return true;
        }

        bool handled = false;
        if (compProps.reduceWill)
        {
            handled |= ReduceWill(pawn, compProps);
        }

        if (compProps.makeSlave)
        {
            handled |= MakeSlave(pawn);
        }

        return handled;
    }

    private static bool ReduceWill(Pawn pawn, HediffCompProperties_ReduceWillOrEnslave compProps)
    {
        Pawn_GuestTracker? guest = pawn.guest;
        if (guest?.IsPrisoner != true)
        {
            return false;
        }

        if (compProps.willReductionAmount <= 0f)
        {
            return true;
        }

        // UnityMathf 保持 1.6 Mathf.Min/Max 的参数顺序语义（NaN 时与 System.Math 不同）。
        float willpowerToReduce = UnityMathf.Min(guest.will, compProps.willReductionAmount);
        guest.will = UnityMathf.Max(0f, guest.will - willpowerToReduce);
        return true;
    }

    private static bool MakeSlave(Pawn pawn)
    {
        Pawn_GuestTracker? guest = pawn.guest;
        Faction? playerFaction = Faction.OfPlayerSilentFail;
        if (guest?.IsPrisoner != true || playerFaction == null)
        {
            return false;
        }

        guest.will = 0f;
        guest.SetGuestStatus(playerFaction, GuestStatus.Slave);
        return true;
    }
}
