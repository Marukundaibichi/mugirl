using Mugirl.Features.Mounting;
using Verse.AI;
using Verse.Sound;

namespace Mugirl.Features.WeaponWheel;

// 火力满载轮射的状态机：抬枪（EquippingPrimary）→ 射击（Firing）→ 换枪（Switching）→ … → 循环冷却（CycleCooldown）。
// 入口是 Verb.TryStartCastOn / TryCastNextBurstShot 的补丁；每 tick 由 TickCombatState 推进动画与冷却。
public sealed partial class CompWeaponWheel
{
    // 保留原判定短路及兼容否决顺序，资格判断一律经过此入口。
    public bool IsFullFirepowerEligible()
    {
        return combat.IsFullFirepowerEligible();
    }

    // 界面状态一次算清：剑舞资格优先于火力满载。
    internal WeaponWheelStatus EvaluateStatus()
    {
        return combat.EvaluateStatus();
    }

    // 外部绘制接管在所有领域校验与日志之前判断，保持原 Prefix 的短路位置。
    internal bool HandleBeforeTryStartCast(Verb verb, LocalTargetInfo castTarget, LocalTargetInfo destinationTarget, bool surpriseAttack, bool canHitNonTargetPawns, bool preventFriendlyFire, bool nonInterruptingSelfCast, out bool result)
    {
        return combat.HandleBeforeTryStartCast(verb, castTarget, destinationTarget, surpriseAttack, canHitNonTargetPawns, preventFriendlyFire, nonInterruptingSelfCast, out result);
    }

    internal void NotifyCastStarted(Verb verb, bool started)
    {
        combat.NotifyCastStarted(verb, started);
    }

    internal void NotifyBurstCompleted(Verb verb)
    {
        combat.NotifyBurstCompleted(verb);
    }

    internal bool ShouldSuppressVanillaWeaponDraw(Thing? weapon)
    {
        return combat.ShouldSuppressVanillaWeaponDraw(weapon);
    }

    internal bool IsCombatDisabledByMount()
    {
        return combat.IsCombatDisabledByMount();
    }

    internal void MaintainCombatFacing()
    {
        combat.MaintainCombatFacing();
    }

    internal bool HasAutoloadingSystem
    {
        get
        {
            return combat.HasAutoloadingSystem;
        }
    }

    // CompAutoloadingSystem 在穿上或脱下时调用（Pawn_ApparelTracker.Notify_ApparelAdded/Removed → Notify_Equipped/Unequipped）。
    internal void NotifyAutoloadingApparelChanged()
    {
        combat.NotifyAutoloadingApparelChanged();
    }

    internal void AbortCombatAndRestorePrimary(bool cancelStance)
    {
        combat.AbortCombatAndRestorePrimary(cancelStance);
    }

    // 兼容会把轮盘备用武器注册进 Pawn 攻击 Verb 池的框架。
    // 普通攻击入口只允许返回真实主武器；轮射直接调用各槽位 Verb，不经过 Pawn.TryGetAttackVerb。
    internal bool TryCorrectAttackVerbSelection(Verb? selectedVerb, bool allowManualCastWeapons, out Verb? correctedVerb)
    {
        return combat.TryCorrectAttackVerbSelection(selectedVerb, allowManualCastWeapons, out correctedVerb);
    }

    // 以下两个方法仅供 WeaponWheelDevLog 使用：汇总私有战斗状态，未开启开发者日志时不会被调用。
    internal string DevDescribeCombat()
    {
        return combat.DevDescribeCombat();
    }
}
