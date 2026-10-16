using Mugirl.Features.Mounting;

namespace Mugirl.Features.Abilities;

// 通用 PawnFlyer 起跳：冲锋击退、扣篮起跳与骑枪冲锋共用（1.6 位于 JobDriver_CastCharge.DoJump，该成员仍保留并转发到这里）。
internal static class MugirlJumpUtility
{
    /// <summary>
    /// 让 <paramref name="pawn"/> 以 PawnFlyer 飞到 <paramref name="targetPosition"/>。骑乘中的雪牛娘先放下骑手；
    /// 放不下、目标格越界或无法生成飞行器时返回 false，Pawn 保持原状。
    /// </summary>
    internal static bool DoJump(Pawn? pawn, IntVec3 targetPosition, VerbProperties? verbProps, Ability? triggeringAbility = null,
        LocalTargetInfo target = default, ThingDef? pawnFlyerOverride = null)
    {
        if (pawn is null || pawn.Destroyed || pawn.Dead)
        {
            return false;
        }

        IntVec3 position = pawn.Position;
        Map? map = pawn.Map;
        if (map is null || !TryDismountMountedRiderBeforeJump(pawn, map))
        {
            return false;
        }

        if (!targetPosition.IsValid || !targetPosition.InBounds(map))
        {
            return false;
        }

        PawnFlyer? pawnFlyer = PawnFlyer.MakeFlyer(
            pawnFlyerOverride ?? ThingDefOf.PawnFlyer,
            pawn,
            targetPosition,
            verbProps?.flightEffecterDef,
            verbProps?.soundLanding,
            false,
            null,
            triggeringAbility,
            target);

        if (pawnFlyer is null)
        {
            return false;
        }

        FleckMaker.ThrowDustPuff((position.ToVector3Shifted().ToNumerics() + Gen.RandomHorizontalVector(0.5f).ToNumerics()).ToUnity(), map, 2f);
        GenSpawn.Spawn(pawnFlyer, targetPosition, map, WipeMode.Vanish);

        SelectionUtility.ReselectIfSelectedInPlaying(pawn, playSound: false, forceDesignatorDeselect: false);

        return true;
    }

    private static bool TryDismountMountedRiderBeforeJump(Pawn pawn, Map map)
    {
        CompMount? comp = MountUtility.GetMountComp(pawn);
        if (comp?.MountedPawn is null)
        {
            return true;
        }

        if (comp.TryDismount(sendMessage: false))
        {
            return true;
        }

        return comp.TryEmergencyDismountNear(pawn.Position, map) || comp.MountedPawn is null;
    }
}
