namespace Mugirl.Features.AdvancedArmor;

// 远程武器的非爆炸伤害在护甲结算前按肩甲数量逐次判定偏转；偏转成功时吸收本次伤害、损耗护甲耐久并迸出火花。
public sealed class CompNeuralShoulderDeflection : ThingComp
{
    private CompProperties_NeuralShoulderDeflection Props => (CompProperties_NeuralShoulderDeflection)props;

    public override void PostPreApplyDamage(ref DamageInfo dinfo, out bool absorbed)
    {
        absorbed = false;
        Pawn? wearer = (parent as Apparel)?.Wearer;
        Map? map = wearer?.Map;
        if (wearer is null || map is null || dinfo.Def?.isExplosive == true || dinfo.Weapon?.IsRangedWeapon != true)
        {
            return;
        }

        int attempts = Math.Max(0, Props.shoulderCount);
        for (int i = 0; i < attempts; i++)
        {
            if (!Rand.Chance(Props.deflectionChancePerShoulder))
            {
                continue;
            }

            absorbed = true;
            parent.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, DurabilityDamage(dinfo.Amount, Props.durabilityDamageFactor, Props.maximumDurabilityDamage)));
            FleckMaker.ThrowMicroSparks(wearer.DrawPos, map);
            return;
        }
    }

    /// <summary>
    /// 偏转一次的耐久损耗：伤害 × 系数向上取整，限制在 [1, max(1, 上限)]。
    /// 取整沿用 Unity 语义：伤害为无穷大或 NaN 时得 int.MinValue，结果为 1（1.7 的饱和转换会得到上限）。
    /// </summary>
    internal static int DurabilityDamage(float damageAmount, float factor, int maximumDamage)
    {
        return UnityMathf.Clamp(UnityMathf.CeilToInt(damageAmount * factor), 1, Math.Max(1, maximumDamage));
    }
}
