namespace Mugirl.Features.AdvancedArmor;

// 神经接入战斗护甲的 XML 配置（1.7/Defs/Apparel/Apparel_0920.xml）：穿戴期间附加的 Hediff。
public sealed class CompProperties_NeuralCombatArmor : CompProperties
{
    public HediffDef? hediff;

    public CompProperties_NeuralCombatArmor()
    {
        compClass = typeof(CompNeuralCombatArmor);
    }
}
