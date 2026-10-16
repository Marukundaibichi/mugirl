using System.Diagnostics.CodeAnalysis;
using Mugirl.Features.Abilities;
using Mugirl.Features.Mounting;

namespace Mugirl.Features.Lances;

/// <summary>冲锋命中：沿途冲锋逐个刺击、眩晕并击退途经的敌人；定点冲锋在终点按骑枪耐久结算一次重击。</summary>
internal static class LanceChargeImpactUtility
{
    // StaticCacheLifecycle: 进程级只读的默认动词属性，只作为击退跳跃的参数，不保存状态。
    private static readonly VerbProperties KnockbackVerbProperties = new();

    internal static bool CanHit([NotNullWhen(true)] Pawn? attacker, [NotNullWhen(true)] Pawn? target, Map? map)
    {
        return attacker is not null && target is not null && target.Spawned && !target.Dead && target != attacker
            && target.Map == map && attacker.HostileTo(target);
    }

    internal static void ApplyLineImpact(Pawn attacker, Pawn target, ThingWithComps lance, IntVec3 chargeCell, Map map)
    {
        if (!CanHit(attacker, target, map) || lance.Destroyed
            || lance.TryGetComp<CompLanceCharge>() is not { } comp)
        {
            return;
        }

        CompProperties_LanceCharge properties = comp.Props;
        Tool? tool = lance.def.tools.NullOrEmpty() ? null : lance.def.tools[0];
        float damage = tool?.AdjustedBaseMeleeDamageAmount(lance, DamageDefOf.Stab) ?? 10f;
        damage *= attacker.GetStatValue(StatDefOf.MeleeDamageFactor);
        DamageInfo damageInfo = new(
            DamageDefOf.Stab,
            damage,
            tool?.armorPenetration ?? 0.5f,
            -1f,
            attacker,
            null,
            lance.def);
        damageInfo.SetTool(tool);
        target.TakeDamage(damageInfo);
        Stun(attacker, target, properties.lineStunTicks);
        DamageLance(lance, properties.lineDurabilityCost);
        MountedMeleeSupport.TryAttackAlongFlyingCharge(
            MountUtility.GetMountComp(attacker),
            target,
            map,
            chargeCell);
        Knockback(attacker, target, chargeCell);
    }

    internal static void ApplyPointImpact(Pawn? attacker, Pawn? target, ThingWithComps? lance, Map? mapOverride = null)
    {
        Map? map = mapOverride ?? attacker?.Map;
        if (!CanHit(attacker, target, map) || lance is null || lance.Destroyed
            || lance.TryGetComp<CompLanceCharge>() is not { } comp)
        {
            return;
        }

        CompProperties_LanceCharge properties = comp.Props;
        Tool? tool = lance.def.tools.NullOrEmpty() ? null : lance.def.tools[0];
        float damage = UnityMathf.Min(lance.HitPoints, properties.maximumPointDamage)
            * attacker.GetStatValue(StatDefOf.MeleeDamageFactor);
        DamageInfo damageInfo = new(
            DamageDefOf.Stab,
            damage,
            tool?.armorPenetration ?? 0.5f,
            -1f,
            attacker,
            null,
            lance.def);
        damageInfo.SetTool(tool);
        target.TakeDamage(damageInfo);
        Stun(attacker, target, properties.pointStunTicks);
        DamageLance(lance, properties.pointDurabilityCost);
        MountedMeleeSupport.TryAttackAlongCharge(MountUtility.GetMountComp(attacker), target);
    }

    /// <summary>
    /// 击退方向：目标相对冲锋格的偏移逐轴取符号，正交与斜向邻格都被推离冲锋格；与冲锋格重合时沿冲锋者朝向推开。
    /// 1.6 先单位化再截断取整，斜向偏移被截成零，只能沿冲锋者朝向推。
    /// </summary>
    internal static IntVec3 KnockbackOffset(IntVec3 targetOffset, IntVec3 fallbackDirection)
    {
        return targetOffset == IntVec3.Zero
            ? fallbackDirection
            : new IntVec3(Math.Sign(targetOffset.x), 0, Math.Sign(targetOffset.z));
    }

    private static void Stun(Pawn attacker, Pawn target, int ticks)
    {
        if (ticks > 0 && target.stances?.stunner is { } stunner && !target.Dead)
        {
            stunner.StunFor(ticks, attacker);
        }
    }

    private static void DamageLance(ThingWithComps lance, int amount)
    {
        if (amount > 0 && !lance.Destroyed)
        {
            lance.TakeDamage(new DamageInfo(DamageDefOf.Deterioration, amount));
        }
    }

    private static void Knockback(Pawn attacker, Pawn target, IntVec3 chargeCell)
    {
        if (target.Dead || !target.Spawned)
        {
            return;
        }

        IntVec3 offset = KnockbackOffset(target.Position - chargeCell, attacker.Rotation.FacingCell);
        JobDriver_CastCharge.DoJump(target, target.Position + offset, KnockbackVerbProperties);
    }
}
