namespace Mugirl.Features.Restraints;

public class HediffComp_DisappearsAndAddHediff : HediffComp_Disappears
{
    public new HediffCompProperties_DisappearsAndAddHediff? Props => props as HediffCompProperties_DisappearsAndAddHediff;

    public override void CompPostPostRemoved()
    {
        base.CompPostPostRemoved();

        // 只有计时结束（CompShouldRemove，原版 messageOnDisappear 用同一判断）才推进到下一阶段。
        // 1.6 在任何移除时都追加：死亡时由 DisappearsOnDeath 移除、卸下洗脑头盔时 CompBrainwashHelmet 清除 Lv1/3/5，
        // 都会立刻把阶段推进到 Lv2/Lv4。
        if (!CompShouldRemove)
        {
            return;
        }

        HediffCompProperties_DisappearsAndAddHediff? compProps = Props;
        Pawn? pawn = Pawn;
        BodyPartRecord? part = parent?.Part;
        if (compProps?.hediffToAddOnDisappear != null && pawn?.health != null && !pawn.Dead && part != null)
        {
            pawn.health.AddHediff(compProps.hediffToAddOnDisappear, part);
        }
    }
}
