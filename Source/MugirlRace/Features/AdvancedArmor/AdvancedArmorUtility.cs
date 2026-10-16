namespace Mugirl.Features.AdvancedArmor;

// 神经接入装备的穿戴判断。调用频率为每次射击、瞄准悬停与子弹命中，穿戴列表通常只有 5–15 件，直接遍历即可。
internal static class AdvancedArmorUtility
{
    internal static bool Wears(Pawn? pawn, ThingDef? apparelDef)
    {
        List<Apparel>? worn = pawn?.apparel?.WornApparel;
        if (worn is null || apparelDef is null)
        {
            return false;
        }

        for (int i = 0; i < worn.Count; i++)
        {
            if (worn[i].def == apparelDef)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool WearsNeuralArmor(Pawn? pawn)
    {
        return Wears(pawn, AdvancedArmorDefOf.Mugirl_NeuralCombatArmor);
    }

    internal static bool WearsNeuralHelmet(Pawn? pawn)
    {
        return Wears(pawn, AdvancedArmorDefOf.Mugirl_NeuralAmplifierHelmet);
    }
}
