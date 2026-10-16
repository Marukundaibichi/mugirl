namespace Mugirl.Features.Milk;

// 挤奶的动画、固定产量和喷乳反馈；流程与采集状态放在同一组件的分部实现中。
public sealed partial class JobDriver_GatherMilk
{
    private const float SelfWorkTicks = 600f;
    private const float OtherWorkTicks = 600f;
    private const float FastMilkingWorkTicks = 60f;

    private float WorkTotal
    {
        get
        {
            if (MugirlMod.Settings?.enableFastMilking == true)
            {
                return FastMilkingWorkTicks;
            }

            return pawn == TargetPawn ? SelfWorkTicks : OtherWorkTicks;
        }
    }

    private static CompMilkProduction? GetComp(Pawn? animal) => animal?.TryGetComp<CompMilkProduction>();

    private static void StartGatherEffects(Pawn doer, Pawn target)
    {
        MilkingAnimation.Start(doer, target);
    }

    private static void TickGatherEffects(Pawn doer, Pawn target)
    {
        MilkingAnimation.Tick(doer, target);
    }

    private static void EndGatherEffects(Pawn doer, Pawn? target)
    {
        MilkingAnimation.End(doer, target);
    }

    // 挤奶使用固定消耗量，避免一次性清空全部奶量。
    private void CompleteGather(Pawn doer)
    {
        if (GetComp(TargetPawn) is not CompMilkProduction comp || !CanGather(comp) || !comp.GatheredFixed(doer, out int milkAmount))
        {
            return;
        }

        if (milkAmount > 0)
        {
            MilkOutputUtility.SpawnStacksNear(MilkDefOf.Mugirl_Milk, milkAmount, doer.Position, doer.Map);
        }

        comp.SpawnMilkEffect();
    }
}
