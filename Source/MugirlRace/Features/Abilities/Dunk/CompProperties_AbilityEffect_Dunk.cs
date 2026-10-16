namespace Mugirl.Features.Abilities;

// 扣篮能力的 XML 配置（1.7/Mods/Biotech/Defs/AbilityDef.xml）：先选受害者，再选落点格。
public class CompProperties_AbilityEffect_Dunk : CompProperties_EffectWithDest
{
    public CompProperties_AbilityEffect_Dunk()
    {
        compClass = typeof(CompAbilityEffect_Dunk);
        destination = AbilityEffectDestination.Selected;
    }
}
