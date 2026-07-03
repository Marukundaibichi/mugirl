using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public class JobDriver_UseItemOn : JobDriver_UseItem
    {
        public static Toil PickupItem(Pawn p, Thing item)
        {
            return new Toil
            {
                initAction = delegate
                {
                    if (item == null || item.Destroyed || p.carryTracker.TryStartCarry(item, 1) <= 0)
                    {
                        p.jobs.curDriver.EndJobWith(JobCondition.Incompletable);
                    }
                },
                defaultCompleteMode = ToilCompleteMode.Instant
            };
        }

        protected TargetIndex iitem = TargetIndex.A;

        protected TargetIndex itar = TargetIndex.B;
        protected TargetIndex appear = TargetIndex.C;
        protected Thing item
        {
            get
            {
                LocalTargetInfo target = base.job.GetTarget(iitem);
                if (!target.HasThing)
                    return null;
                return target.Thing;
            }
        }

        protected Thing tar
        {
            get
            {
                LocalTargetInfo target = base.job.GetTarget(itar);
                if (!target.HasThing)
                    return null;
                return target.Thing;
            }
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            Thing itemToUse = item;
            Thing targetThing = tar;
            if (targetThing == null)
            {
                if (itemToUse == null)
                {
                    yield break;
                }

                foreach (var toil in base.MakeNewToils())
                {
                    yield return toil;
                }
            }
            else
            {
                Pawn other = ResolveTargetPawn(targetThing);
                if (itemToUse == null || other?.apparel == null)
                {
                    yield break;
                }

                this.FailOnDespawnedNullOrForbidden(itar);
                if (!other.Dead)
                    this.FailOnAggroMentalState(itar);
                yield return Toils_Reserve.Reserve(itar);

                if ((pawn.inventory != null) && pawn.inventory.Contains(itemToUse))
                {
                    yield return Toils_Misc.TakeItemFromInventoryToCarrier(pawn, iitem);
                }
                else if (itemToUse.Spawned)
                {
                    yield return Toils_Reserve.Reserve(iitem);
                    yield return Toils_Goto.GotoThing(iitem, PathEndMode.ClosestTouch).FailOnForbidden(iitem);
                    yield return PickupItem(pawn, itemToUse);
                }
                else
                {
                    yield break;
                }

                yield return Toils_Goto.GotoThing(itar, PathEndMode.Touch);

                yield return new Toil
                {
                    initAction = delegate
                    {
                        if (!other.Dead)
                            PawnUtility.ForceWait(other, 60);
                    },
                    defaultCompleteMode = ToilCompleteMode.Delay,
                    defaultDuration = 60
                };

                yield return new Toil
                {
                    initAction = delegate
                    {
                        Thing effective_item = itemToUse;
                        if ((effective_item as Apparel) != null)
                        {
                            Thing dropped_thing;
                            if (pawn.carryTracker.TryDropCarriedThing(pawn.Position, ThingPlaceMode.Near, out dropped_thing))
                                effective_item = dropped_thing as Apparel;
                            else
                            {
                                effective_item = null;
                            }
                        }

                        if (effective_item != null)
                        {
                            var eff = effective_item.TryGetComp<CompUseEffect>();
                            if (eff != null)
                                eff.DoEffect(other);
                        }
                    },
                    defaultCompleteMode = ToilCompleteMode.Instant
                };
            }
        }

        private static Pawn ResolveTargetPawn(Thing target)
        {
            if (target is Pawn pawn)
            {
                return pawn;
            }

            if (target is Corpse corpse)
            {
                return corpse.InnerPawn;
            }

            return null;
        }
    }
}
