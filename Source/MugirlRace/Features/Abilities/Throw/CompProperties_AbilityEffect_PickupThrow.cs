namespace Mugirl.Features.Abilities;

// 举起投掷能力的 XML 配置（1.7/Mods/Biotech/Defs/AbilityDef.xml）。
public class CompProperties_AbilityEffect_PickupThrow : CompProperties_AbilityEffect
{
    public CompProperties_AbilityEffect_PickupThrow()
    {
        compClass = typeof(CompAbilityEffect_PickupThrow);
    }
}
