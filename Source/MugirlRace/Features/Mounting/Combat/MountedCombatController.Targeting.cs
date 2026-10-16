using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Features.Mounting;

// 炮塔索敌：优先载具当前的近战目标，否则以载具为搜索者调用原版 AttackTargetFinder。
public static partial class MountedCombatController
{
    private const TargetScanFlags TurretScanFlags =
        TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable | TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedNonBurning;

    private static Thing? FindBestTarget(CompMount comp, Verb verb)
    {
        Pawn? carrier = comp.MooPawn;
        if (carrier?.Map is null)
        {
            return null;
        }

        Thing? meleeTarget = MountedMeleeSupport.CurrentMeleeTarget(carrier);
        if (IsValidTarget(carrier, verb, meleeTarget, allowPointBlank: true))
        {
            return meleeTarget;
        }

        MountedAttackTargetSearcher searcher = new(comp, verb);
        // 整个搜索期间施放者保持为载具：原版 CanShootAtFromCurrentPosition 以 caster.Map 判断射线，
        // 验证器内的嵌套作用域结束后也不能把施放者改回未生成的骑手（见 MountedVerbScope）。
        using (MountedCasterScope(comp, verb))
        {
            return AttackTargetFinder.BestShootTargetFromCurrentPosition(searcher, TurretScanFlags, thing => IsValidTarget(carrier, verb, thing), 0f, verb.EffectiveRange)?.Thing;
        }
    }

    private static bool IsValidTarget(Pawn carrier, Verb verb, [NotNullWhen(true)] Thing? target, bool allowPointBlank = false)
    {
        if (carrier.Map is null || target is null || target.Destroyed || target.Map != carrier.Map)
        {
            return false;
        }

        if (target == carrier || !carrier.HostileTo(target))
        {
            return false;
        }

        if (target is Pawn pawn && (pawn.Dead || pawn.Downed))
        {
            return false;
        }

        bool pointBlank = allowPointBlank && target.Position.AdjacentTo8WayOrInside(carrier.Position);
        using (new MountedVerbScope(verb, carrier))
        using (new MountedMinRangeOverride(verb, pointBlank ? 0f : null))
        {
            return verb.TryFindShootLineFromTo(carrier.Position, target, out _);
        }
    }
}
