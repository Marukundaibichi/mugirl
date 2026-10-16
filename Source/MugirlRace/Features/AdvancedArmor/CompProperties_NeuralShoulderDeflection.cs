namespace Mugirl.Features.AdvancedArmor;

// 神经偏转肩甲的 XML 配置（1.7/Defs/Apparel/Apparel_0920.xml）：每侧肩甲独立判定一次子弹偏转。
public sealed class CompProperties_NeuralShoulderDeflection : CompProperties
{
    public float deflectionChancePerShoulder = 0.12f;
    public int shoulderCount = 2;
    public float durabilityDamageFactor = 0.04f;
    public int maximumDurabilityDamage = 8;

    public CompProperties_NeuralShoulderDeflection()
    {
        compClass = typeof(CompNeuralShoulderDeflection);
    }
}
