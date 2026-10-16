namespace Mugirl.Features.AdvancedArmor;

// 神经战斗护甲功能的 Def。
[DefOf]
internal static class AdvancedArmorDefOf
{
    public static ThingDef Mugirl_NeuralCombatArmor = null!;

    public static ThingDef Mugirl_NeuralAmplifierHelmet = null!;

    static AdvancedArmorDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(AdvancedArmorDefOf));
}
