using RimWorld;
using Verse;

namespace Mugirl
{
    /// <summary>
    /// 仅供明确采用尾巴协同 Maneuver 的机械体使用；不挂全局 Harmony。
    /// </summary>
    public sealed class Verb_MeleeAttack_SegmentedTailAssist : Verb_MeleeAttackDamage
    {
        protected override bool TryCastShot()
        {
            Pawn casterPawn = CasterPawn;
            if (casterPawn != null && casterPawn.Spawned && !casterPawn.stances.FullBodyBusy)
            {
                casterPawn.TryGetComp<Comp_SegmentedMechTail>()?.NotifyMeleeAttack(CurrentTarget.Thing);
            }
            return base.TryCastShot();
        }

        protected override DamageWorker.DamageResult ApplyMeleeDamageToTarget(
            LocalTargetInfo target)
        {
            Comp_SegmentedMechTail tailComp =
                CasterPawn?.TryGetComp<Comp_SegmentedMechTail>();
            DamageWorker.DamageResult primaryResult =
                base.ApplyMeleeDamageToTarget(target);
            if (tailComp == null
                || !tailComp.TryApplyAssistDamage(target.Thing, out DamageWorker.DamageResult assistResult))
            {
                return primaryResult;
            }

            MergeDamageResults(primaryResult, assistResult);
            return primaryResult;
        }

        private static void MergeDamageResults(
            DamageWorker.DamageResult primary,
            DamageWorker.DamageResult assist)
        {
            if (primary == null || assist == null)
            {
                return;
            }

            primary.totalDamageDealt += assist.totalDamageDealt;
            primary.wounded |= assist.wounded;
            primary.headshot |= assist.headshot;
            primary.stunned |= assist.stunned;
            primary.deflected = primary.deflected && assist.deflected;
            if (primary.hitThing == null)
            {
                primary.hitThing = assist.hitThing;
            }

            if (assist.parts != null)
            {
                for (int i = 0; i < assist.parts.Count; i++)
                {
                    primary.AddPart(assist.hitThing, assist.parts[i]);
                }
            }
            if (assist.hediffs != null)
            {
                for (int i = 0; i < assist.hediffs.Count; i++)
                {
                    primary.AddHediff(assist.hediffs[i]);
                }
            }
        }
    }
}
