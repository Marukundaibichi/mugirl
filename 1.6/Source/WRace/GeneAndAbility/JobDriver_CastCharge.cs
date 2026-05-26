using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using RimWorld;
using Verse.AI;
using System.Linq;

namespace MooGirl
{
    public class JobDriver_CastCharge : JobDriver
    {
        private const float SmallPawnThreshold = 1f;
        private HashSet<Pawn> hitPawns = new HashSet<Pawn>();

        // 生成烟雾的间隔
        private const int SmokeGenerationInterval = 10;  // 每10 ticks生成一次
        private int ticksSinceSmokeGeneration = 0;  // 计时器

        // 自定义Hediff
        private Hediff mooGirlChargeHediff;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(job.targetA, job);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Pawn target = job.targetA.Thing as Pawn;
            this.FailOnDestroyedOrNull(TargetIndex.A);

            // 1️⃣ 添加 MooGirl_Charge Hediff 给自己
            this.mooGirlChargeHediff = HediffMaker.MakeHediff(MooGirl_DefOf.MooGirl_Charge, pawn);
            pawn.health.AddHediff(mooGirlChargeHediff);

            // 2️⃣ 在自己背后生成 3~5 团烟雾
            GenerateSmokeBehindPawn();

            // 3️⃣ 正常走向目标
            Toil gotoToil = Toils_Goto.GotoThing(TargetIndex.A, PathEndMode.Touch);
            gotoToil.tickAction = () =>
            {
                // 每 tick 判定当前位置附近 Pawn
                foreach (Pawn p in GenRadial.RadialDistinctThingsAround(
                    pawn.Position,
                    pawn.Map,
                    2f,
                    useCenter: true).OfType<Pawn>())
                {
                    if (p == pawn || p == target) continue;
                    if (hitPawns.Contains(p)) continue;

                    DamageInfo dinfo;
                    if (p.BodySize < SmallPawnThreshold)
                    {
                        dinfo = new DamageInfo(DamageDefOf.Crush, Rand.Range(3f, 5f), 0f, -1, pawn);
                    }
                    else
                    {
                        dinfo = new DamageInfo(DamageDefOf.Blunt, Rand.Range(2f, 3f), 0f, -1, pawn);
                    }

                    p.TakeDamage(dinfo);
                    hitPawns.Add(p);

                    // 设置飞行目标位置
                    IntVec3 flyTargetPosition;
                    Vector3 direction = (p.Position.ToVector3() - pawn.Position.ToVector3()).normalized;
                    flyTargetPosition = p.Position + direction.ToIntVec3() * 1;  // 较近的目标位置

                    // 创建一个虚拟的 VerbProperties 或从现有能力中获取 verbProps
                    VerbProperties verbProps = new VerbProperties();  // 这里需要根据实际的游戏系统来调整

                    // 确保目标未死亡才会让其飞起来
                    if (!p.Dead)
                    {
                        // 调用我们自定义的 DoJump 方法
                        if (DoJump(p, flyTargetPosition, verbProps))
                        {
                            Log.Message($"Pawn {p.Name} successfully jumped to {flyTargetPosition}.");
                        }
                        else
                        {
                            Log.Warning($"Failed to make Pawn {p.Name} jump.");
                        }
                    }
                }

                // 每 10 ticks 在当前 Pawn 位置生成烟雾
                if (++ticksSinceSmokeGeneration >= SmokeGenerationInterval)
                {
                    ticksSinceSmokeGeneration = 0;
                    GenerateSmokeAtPawnPosition();
                }
            };

            yield return gotoToil;

            // 4️⃣ 到达目标后的终结攻击
            Toil attack = new Toil();
            attack.initAction = () =>
            {
                // 1️⃣ 移除最初添加的加速 Hediff
                Hediff speedHediff = pawn.health.hediffSet.GetFirstHediffOfDef(MooGirl_DefOf.MooGirl_Charge);
                if (speedHediff != null)
                {
                    pawn.health.RemoveHediff(speedHediff); // 移除加速 Hediff
                }

                // 如果目标Pawn已消失（未生成）则跳过
                if (target?.Spawned != true) return;

                // 2️⃣ 对目标Pawn造成伤害
                target.TakeDamage(new DamageInfo(
                    DamageDefOf.Cut,
                    50f,
                    0f,
                    -1,
                    pawn));

                // 3️⃣ 添加 stun 状态
                var stun = HediffMaker.MakeHediff(MooGirl_DefOf.MooGirl_Stun, target);
                target.health.AddHediff(stun);

                // 4️⃣ 让目标Pawn被撞飞
                // 计算撞击方向，并归一化
                Vector3 direction = (target.Position.ToVector3() - pawn.Position.ToVector3()).normalized;
                IntVec3 flyTargetPosition = target.Position + direction.ToIntVec3() * 6;  // 设置飞行目标

                // 创建一个虚拟的 VerbProperties 或从现有能力中获取 verbProps
                VerbProperties verbProps = new VerbProperties();  // 根据实际的游戏系统来调整

                // 确保目标未死亡才会让其飞起来
                if (!target.Dead)
                {
                    // 调用 DoJump 让目标飞起来
                    if (DoJump(target, flyTargetPosition, verbProps))
                    {
                        Log.Message($"Pawn {target.Name} successfully jumped to {flyTargetPosition}.");
                    }
                    else
                    {
                        Log.Warning($"Failed to make Pawn {target.Name} jump.");
                    }
                }
            };
            attack.defaultCompleteMode = ToilCompleteMode.Instant;

            job.ability.StartCooldown(job.ability.def.cooldownTicksRange.RandomInRange);

            yield return attack;
        }

        // 在自己背后生成 3~5 团烟雾
        private void GenerateSmokeBehindPawn()
        {
            int smokeCount = Rand.RangeInclusive(3, 5);
            for (int i = 0; i < smokeCount; i++)
            {
                // 计算从pawn到目标的反方向（背后）
                Vector3 direction = (pawn.DrawPos - job.targetA.Thing.DrawPos).normalized;  // 归一化方向向量
                IntVec3 smokePosition = pawn.Position + direction.ToIntVec3() * (i + 1);  // 计算烟雾位置

                // 使用FleckMaker来生成烟雾，调整烟雾的速度、旋转等
                FleckMaker.ThrowSmoke(smokePosition.ToVector3Shifted(), pawn.Map, Rand.Range(1f, 1.5f));
            }
        }

        // 在当前 Pawn 位置生成烟雾
        private void GenerateSmokeAtPawnPosition()
        {
            // 在pawn位置生成烟雾，调整大小，速度等
            FleckMaker.ThrowSmoke(pawn.Position.ToVector3Shifted(), pawn.Map, Rand.Range(1.5f, 2f));
        }

        public static bool DoJump(Pawn pawn, IntVec3 targetPosition, VerbProperties verbProps, Ability triggeringAbility = null, LocalTargetInfo target = default(LocalTargetInfo), ThingDef pawnFlyerOverride = null)
        {
            if (pawn == null || pawn.Destroyed || pawn.Dead)
            {
                return false;
            }

            // 获取当前Pawn的起始位置
            IntVec3 position = pawn.Position;
            Map map = pawn.Map;
            if (map == null)
            {
                return false;
            }

            if (!TryDismountMountedRiderBeforeJump(pawn, map))
            {
                Log.Warning($"Failed to dismount rider before making Pawn {pawn.Name} jump.");
                return false;
            }

            // 检查目标位置是否合法
            if (!targetPosition.IsValid || !targetPosition.InBounds(map))
            {
                Log.Warning($"Target position {targetPosition} is out of bounds or invalid.");
                return false;
            }

            // 直接生成一个跳跃的PawnFlyer对象，不考虑死亡状态
            PawnFlyer pawnFlyer = PawnFlyer.MakeFlyer(
                pawnFlyerOverride ?? ThingDefOf.PawnFlyer,  // 如果没有提供飞行物定义，则使用默认定义
                pawn,
                targetPosition,
                verbProps?.flightEffecterDef,
                verbProps?.soundLanding,
                false,  // 无需考虑携带物品
                null,  // 无需考虑携带物品
                triggeringAbility,
                target
            );

            if (pawnFlyer != null)
            {
                // 跳跃前的效果：例如在起始位置制造尘土效果
                FleckMaker.ThrowDustPuff(position.ToVector3Shifted() + Gen.RandomHorizontalVector(0.5f), map, 2f);

                // 创建并生成飞行对象
                GenSpawn.Spawn(pawnFlyer, targetPosition, map, WipeMode.Vanish);

                // 如果当前Pawn被选中，则重新选择它
                if (Find.Selector.IsSelected(pawn))
                {
                    Find.Selector.Select(pawn, false, false);
                }

                Log.Message($"Pawn {pawn.Name} successfully jumped to {targetPosition}.");
                return true;
            }
            else
            {
                Log.Warning($"Failed to create PawnFlyer for {pawn.Name}.");
                return false;
            }
        }

        private static bool TryDismountMountedRiderBeforeJump(Pawn pawn, Map map)
        {
            Comp_MooGirlMount comp = MountedPawnUtility.GetMountComp(pawn);
            if (comp?.MountedPawn == null)
            {
                return true;
            }

            if (comp.TryDismount(sendMessage: false))
            {
                return true;
            }

            return comp.TryEmergencyDismountNear(pawn.Position, map) || comp.MountedPawn == null;
        }
    }
}
