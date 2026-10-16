using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Features.Abilities;

// 冲锋 Job（XML driverClass）：冲向目标，沿途撞飞经过的 Pawn，抵达后重击并击晕目标。
public class JobDriver_CastCharge : JobDriver
{
    private const float SmallPawnThreshold = 1f;
    private const float ImpactRadius = 2f;
    private const int SmokeGenerationInterval = 10;
    private const int PassingKnockbackDistance = 1;
    private const int FinalKnockbackDistance = 6;
    private static readonly VerbProperties DefaultJumpVerbProps = new();

    // 不存档：读档后已撞过的 Pawn 可能再被撞一次（与 1.6 相同）。
    private readonly HashSet<Pawn> hitPawns = [];
    // 每 tick 先收集本次命中的 Pawn 再结算：击退会把 Pawn 移出格子的实体列表，边遍历边击退会跳过同格的下一个 Pawn。
    private readonly List<Pawn> passingCandidates = [];
    private int ticksSinceSmokeGeneration;

    private Pawn? TargetPawn => job.GetTarget(TargetIndex.A).Thing as Pawn;

    /// <summary>1.6 的公开入口，骑枪冲锋仍在使用；实现见 <see cref="MugirlJumpUtility.DoJump"/>。</summary>
    public static bool DoJump(Pawn? pawn, IntVec3 targetPosition, VerbProperties? verbProps, Ability? triggeringAbility = null,
        LocalTargetInfo target = default, ThingDef? pawnFlyerOverride = null)
    {
        return MugirlJumpUtility.DoJump(pawn, targetPosition, verbProps, triggeringAbility, target, pawnFlyerOverride);
    }

    public override bool TryMakePreToilReservations(bool errorOnFailed)
    {
        Pawn? target = TargetPawn;
        return CanChargeTarget(target) && pawn.Reserve(target, job, 1, -1, null, errorOnFailed);
    }

    // 只在 Job 开始时执行一次。1.6 把这些副作用写在 MakeNewToils 里，而读档时 JobDriver.ExposeData 会重新调用
    // MakeNewToils（SetupToils），导致冲锋途中读档会重新冷却能力并再次放烟。
    public override void Notify_Starting()
    {
        base.Notify_Starting();
        EnsureChargeHediff();
        GenerateSmokeBehindPawn(TargetPawn);
        job.ability?.StartCooldown(job.ability.def.cooldownTicksRange.RandomInRange);
    }

    protected override IEnumerable<Toil> MakeNewToils()
    {
        this.FailOnDestroyedOrNull(TargetIndex.A);
        this.FailOn(() => !CanChargeTarget(TargetPawn));

        AddFinishAction(_ => RemoveChargeHediff());

        Toil gotoToil = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
        gotoToil.tickAction = () =>
        {
            ApplyPassingImpact(TargetPawn);
            TickSmokeTrail();
        };
        yield return gotoToil;

        Toil attack = new()
        {
            initAction = () => ApplyFinalImpact(TargetPawn),
            defaultCompleteMode = ToilCompleteMode.Instant
        };
        yield return attack;
    }

    private bool CanChargeTarget([NotNullWhen(true)] Pawn? target)
    {
        return pawn is not null
            && target is not null
            && target != pawn
            && target.Spawned
            && !target.Destroyed
            && !target.Dead
            && target.Map == pawn.Map;
    }

    private void ApplyPassingImpact(Pawn? target)
    {
        Map? map = pawn.Map;
        if (map is null)
        {
            return;
        }

        // 冲锋持续期间每 tick 执行；按 RadialPattern 直接遍历格内实体，
        // 等价于 RadialDistinctThingsAround(useCenter:true)——pawn 恒为 1×1 单格，
        // 原版的占格去重集合对本分支无作用，hitPawns 已完成命中去重——且不分配枚举器。
        passingCandidates.Clear();
        int cellCount = GenRadial.NumCellsInRadius(ImpactRadius);
        for (int i = 0; i < cellCount; i++)
        {
            IntVec3 cell = GenRadial.RadialPattern[i] + pawn.Position;
            if (!cell.InBounds(map))
            {
                continue;
            }

            List<Thing> thingList = cell.GetThingList(map);
            for (int j = 0; j < thingList.Count; j++)
            {
                if (thingList[j] is Pawn hitPawn && hitPawn != pawn && hitPawn != target && !hitPawns.Contains(hitPawn))
                {
                    passingCandidates.Add(hitPawn);
                }
            }
        }

        for (int i = 0; i < passingCandidates.Count; i++)
        {
            Pawn hitPawn = passingCandidates[i];
            // 同一 Pawn 可能出现在多个格子的列表中（极少），只结算一次。
            if (!hitPawns.Add(hitPawn))
            {
                continue;
            }

            DamageInfo damageInfo = hitPawn.BodySize < SmallPawnThreshold
                ? new DamageInfo(DamageDefOf.Crush, Rand.Range(3f, 5f), 0f, -1, pawn)
                : new DamageInfo(DamageDefOf.Blunt, Rand.Range(2f, 3f), 0f, -1, pawn);

            hitPawn.TakeDamage(damageInfo);
            if (!hitPawn.Dead && hitPawn.Spawned)
            {
                KnockBack(hitPawn, PassingKnockbackDistance);
            }
        }

        passingCandidates.Clear();
    }

    private void ApplyFinalImpact(Pawn? target)
    {
        RemoveChargeHediff();

        if (!CanChargeTarget(target))
        {
            return;
        }

        target.TakeDamage(new DamageInfo(DamageDefOf.Cut, 50f, 0f, -1, pawn));
        if (AbilitiesDefOf.Mugirl_Stun is { } stunDef)
        {
            target.health.AddHediff(HediffMaker.MakeHediff(stunDef, target));
        }

        if (!target.Dead)
        {
            KnockBack(target, FinalKnockbackDistance);
        }
    }

    private void KnockBack(Pawn victim, int distance)
    {
        Map? map = victim.Map;
        if (map is not null && ChargeKnockbackUtility.TryFindKnockbackCell(map, pawn.Position, victim.Position, distance, out IntVec3 cell))
        {
            MugirlJumpUtility.DoJump(victim, cell, DefaultJumpVerbProps);
        }
    }

    private void EnsureChargeHediff()
    {
        HediffDef? chargeDef = AbilitiesDefOf.Mugirl_Charge;
        if (pawn?.health is null || chargeDef is null)
        {
            return;
        }

        if (pawn.health.hediffSet.GetFirstHediffOfDef(chargeDef) is null)
        {
            pawn.health.AddHediff(HediffMaker.MakeHediff(chargeDef, pawn));
        }
    }

    private void RemoveChargeHediff()
    {
        HediffDef? chargeDef = AbilitiesDefOf.Mugirl_Charge;
        if (pawn?.health is null || chargeDef is null)
        {
            return;
        }

        Hediff? speedHediff;
        while ((speedHediff = pawn.health.hediffSet.GetFirstHediffOfDef(chargeDef)) is not null)
        {
            pawn.health.RemoveHediff(speedHediff);
        }
    }

    private void GenerateSmokeBehindPawn(Pawn? target)
    {
        Map? map = pawn.Map;
        if (map is null || target is null)
        {
            return;
        }

        Vector3 awayFromTarget = pawn.DrawPos.ToNumerics() - target.DrawPos.ToNumerics();
        int smokeCount = Rand.RangeInclusive(3, 5);
        for (int i = 0; i < smokeCount; i++)
        {
            IntVec3 smokePosition = pawn.Position + ChargeKnockbackUtility.RoundedOffset(awayFromTarget, i + 1);
            FleckMaker.ThrowSmoke(smokePosition.ToVector3Shifted(), map, Rand.Range(1f, 1.5f));
        }
    }

    private void TickSmokeTrail()
    {
        if (++ticksSinceSmokeGeneration < SmokeGenerationInterval)
        {
            return;
        }

        ticksSinceSmokeGeneration = 0;
        if (pawn.Map is { } map)
        {
            FleckMaker.ThrowSmoke(pawn.Position.ToVector3Shifted(), map, Rand.Range(1.5f, 2f));
        }
    }
}
