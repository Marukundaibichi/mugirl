using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class JobDriver_CrackBondageGear : JobDriver
    {
        protected TargetIndex iitem = TargetIndex.A;  // 破解工具
        protected TargetIndex appear = TargetIndex.B; // 目标装备

        // 获取物品和装备
        protected Thing item
        {
            get { return base.job.GetTarget(iitem).Thing; }
        }

        protected Apparel targetApparel
        {
            get { return (Apparel)base.job.GetTarget(appear).Thing; }
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            return pawn.Reserve(item, job, 1, -1, null, errorOnFailed) &&
                   pawn.Reserve(targetApparel, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            // 确保目标装备是奴隶装备
            Apparel apparel = targetApparel as Apparel;
            if (apparel == null)
            {
                yield break;
            }

            // 预留装备
            yield return Toils_Reserve.Reserve(appear);

            // 如果 pawn 已经拥有破解工具，则直接使用
            if (pawn.inventory != null && pawn.inventory.Contains(item))
            {
                yield return Toils_Misc.TakeItemFromInventoryToCarrier(pawn, iitem);
            }
            else
            {
                // 如果没有持有物品，走到物品处并捡起
                yield return Toils_Reserve.Reserve(iitem);
                yield return Toils_Goto.GotoThing(iitem, PathEndMode.ClosestTouch).FailOnForbidden(iitem);
                yield return new Toil
                {
                    initAction = () =>
                    {
                        pawn.carryTracker.TryStartCarry(item, 1);
                    },
                    defaultCompleteMode = ToilCompleteMode.Instant
                };
            }

            // 走向目标装备进行破解
            yield return Toils_Goto.GotoThing(appear, PathEndMode.Touch);

            // 等待和进度条
            yield return Toils_General.Wait(60)
                .WithProgressBarToilDelay(appear)
                .FailOnDespawnedNullOrForbidden(appear); 

            // 获取目标装备的破解效果并执行
            yield return new Toil
            {
                initAction = () =>
                {
                    CompTargetCrackBondageGear compEffect = item.TryGetComp<CompTargetCrackBondageGear>();
                    if (compEffect != null)
                    {
                        // 执行破解效果
                        compEffect.DoEffectOn(pawn, targetApparel);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };

            // 破解的结果
            yield return new Toil
            {
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }
    }
}
