using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.WeaponWheel;

// 连携剑舞：轮盘中全为近战武器时，近战命中有概率瞬移到目标身后，冲刺动画越过目标时才结算伤害，
// 随后施加冲撞伤害与加速 Hediff，并把下一把近战武器轮换到主手。
// 时序：TryCastNextBurstShot 前缀延迟结算（TryDelaySwordDanceStrikeUntilImpact）→ 冲刺到达命中进度时
// 经 SwordDanceDelayedStrike 重新调用 TryCastNextBurstShot（ResolveDelayedSwordDanceStrike）→ Verb_MeleeAttack.TryCastShot 后缀确认 →
// 下一 tick 结算换手（TickPendingSwordDanceCompletion）。
public sealed partial class CompWeaponWheel
{
    public bool IsSwordDanceEligible()
    {
        return swordDance.IsSwordDanceEligible();
    }

    internal bool TryDelaySwordDanceStrikeUntilImpact(Verb verb)
    {
        return swordDance.TryDelaySwordDanceStrikeUntilImpact(verb);
    }

    internal bool IsSwordDanceStrikeAwaitingImpact(Verb? verb)
    {
        return swordDance.IsSwordDanceStrikeAwaitingImpact(verb);
    }

    // Pawn.DrawPos 后缀调用（含渲染工作线程）：只读冲刺字段，把原版位置相对补间根的偏移平移到冲刺根位置上。
    internal bool TryGetSwordDanceDashDrawPosition(UVector3 vanillaDrawPosition, out UVector3 drawPosition)
    {
        return swordDance.TryGetSwordDanceDashDrawPosition(vanillaDrawPosition, out drawPosition);
    }

    internal bool MaintainSwordDanceDashFacing()
    {
        return swordDance.MaintainSwordDanceDashFacing();
    }

    internal float SwordDanceDashAimAngle(float fallbackAngle)
    {
        return swordDance.SwordDanceDashAimAngle(fallbackAngle);
    }

    internal bool TryGetSwordDanceDashHeldWeapon([NotNullWhen(true)] out ThingWithComps? weapon, out float aimAngle)
    {
        return swordDance.TryGetSwordDanceDashHeldWeapon(out weapon, out aimAngle);
    }

    internal void NotifySwordDanceStrikeResolved(Verb? verb)
    {
        swordDance.NotifySwordDanceStrikeResolved(verb);
    }

    internal bool NotifyMeleeAttackCompleted(Verb verb)
    {
        return swordDance.NotifyMeleeAttackCompleted(verb);
    }

    internal bool ShouldSuppressExternalMeleeAttackAnimation()
    {
        return swordDance.ShouldSuppressExternalMeleeAttackAnimation();
    }
}
