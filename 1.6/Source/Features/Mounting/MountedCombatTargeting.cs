using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public static partial class MountedCombatController
    {
        private static Thing FindBestTarget(Comp_MugirlMount comp, Verb verb)
        {
            if (comp == null || verb == null)
            {
                return null;
            }

            Pawn carrier = comp.MooPawn;
            if (carrier?.Map == null)
            {
                return null;
            }

            Thing meleeTarget = MountedPawnMeleeSupport.CurrentMeleeTarget(carrier);
            if (IsValidTarget(carrier, verb, meleeTarget, allowPointBlank: true))
            {
                return meleeTarget;
            }

            MountedAttackTargetSearcher searcher = new MountedAttackTargetSearcher(comp, verb);
            TargetScanFlags flags = TargetScanFlags.NeedThreat | TargetScanFlags.NeedAutoTargetable | TargetScanFlags.NeedLOSToAll | TargetScanFlags.NeedNonBurning;
            using (MountedCasterScope(comp, verb))
            {
                return AttackTargetFinder.BestShootTargetFromCurrentPosition(searcher, flags, thing => IsValidTarget(carrier, verb, thing), 0f, verb.EffectiveRange)?.Thing;
            }
        }

        private static bool IsValidTarget(Pawn carrier, Verb verb, Thing target, bool allowPointBlank = false)
        {
            if (carrier?.Map == null || verb == null || target == null || target.Destroyed || target.Map != carrier.Map)
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

            if (allowPointBlank && target.Position.AdjacentTo8WayOrInside(carrier.Position))
            {
                using (new MountedVerbScope(verb, carrier, OriginalCasterFor(verb, null)))
                using (new MountedMinRangeOverride(verb, 0f))
                {
                    return verb.TryFindShootLineFromTo(carrier.Position, target, out _);
                }
            }

            using (new MountedVerbScope(verb, carrier, OriginalCasterFor(verb, null)))
            {
                return verb.TryFindShootLineFromTo(carrier.Position, target, out _);
            }
        }
    }
}
