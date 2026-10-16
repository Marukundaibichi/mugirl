namespace Mugirl.Features.Abilities;

// 强制任务能力的 XML 配置（1.7/Mods/Biotech/Defs/AbilityDef.xml）：指定影响半径、目标 Job 和可选持续时间。
public class CompProperties_AbilityEffect_ForceJobInRadius : CompProperties_AbilityEffect
{
    public float radius = 6f;
    public JobDef? jobDef;

    // 强制任务最多维持的 tick；不大于 0 时沿用 JobDef 自身的结束条件。
    public int durationTick;

    public CompProperties_AbilityEffect_ForceJobInRadius()
    {
        compClass = typeof(CompAbilityEffect_ForceJobInRadius);
    }
}
