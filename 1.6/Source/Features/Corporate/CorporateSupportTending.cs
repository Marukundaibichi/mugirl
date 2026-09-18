using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace Mugirl
{
    // 巨企支援小队的随队补给与战场医疗。
    // 补给生成后直接进背包；进食交给原版 LordDuty 子树的 UrgentlyHungry 兜底
    // （JobGiver_GetFood 优先吃背包食物），医疗由 Mugirl_CorporateSupportHunt
    // duty 里的 JobGiver_CorporateSupportTend 在索敌之后执行。
    internal static class CorporateSupportUtility
    {
        public const int SurvivalPackCount = 2;
        public const int MedicineCount = 2;

        public static void EquipFieldSupplies(Pawn pawn)
        {
            if (pawn?.inventory == null) return;
            AddToInventory(pawn, ThingDefOf.MealSurvivalPack, SurvivalPackCount, null);
            // 医药固定普通品质，与每周赠礼的医药一致。
            AddToInventory(pawn, ThingDefOf.MedicineIndustrial, MedicineCount, QualityCategory.Normal);
        }

        private static void AddToInventory(Pawn pawn, ThingDef def, int count, QualityCategory? quality)
        {
            Thing thing = ThingMaker.MakeThing(def);
            if (quality.HasValue) thing.TryGetComp<CompQuality>()?.SetQuality(quality.Value, ArtGenerationContext.Outsider);
            thing.stackCount = count;
            if (!pawn.inventory.innerContainer.TryAdd(thing))
            {
                thing.Destroy(DestroyMode.Vanish);
            }
        }
    }

    // 支援小队空闲医疗：索敌节点拿不到目标且本地安全时，为自己或附近受伤的
    // 友方小人（队员、玩家殖民者与玩家派系奴隶）处理伤口。医药只从自己的背包取，
    // 不到玩家库存或地图上拿药。
    public class JobGiver_CorporateSupportTend : ThinkNode_JobGiver
    {
        private const float MaxPatientRadius = 45f;
        private const int MaxThreatRegionDepth = 5;

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || pawn.Map == null || !pawn.RaceProps.Humanlike
                || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
            {
                return null;
            }

            List<Pawn> patients = CollectPatients(pawn);
            if (patients.Count == 0) return null;
            if (HasNearbyActiveThreat(pawn)) return null;

            patients.SortBy(p => pawn.Position.DistanceToSquared(p.Position));
            for (int i = 0; i < patients.Count; i++)
            {
                Pawn patient = patients[i];
                if (!pawn.CanReach(patient, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                if (!pawn.CanReserve(patient, 1, -1)) continue;

                Thing medicine = BestInventoryMedicine(pawn, patient);
                Job job = medicine != null
                    ? JobMaker.MakeJob(JobDefOf.TendPatient, patient, medicine)
                    : JobMaker.MakeJob(JobDefOf.TendPatient, patient);
                // 单次处理后结束，避免原版 FindMoreMedicineToil 把 NPC 引向地图上的玩家医药库存。
                job.endAfterTendedOnce = true;
                return job;
            }
            return null;
        }

        private static List<Pawn> CollectPatients(Pawn medic)
        {
            List<Pawn> patients = new List<Pawn>();
            float maxDistSq = MaxPatientRadius * MaxPatientRadius;
            CollectFrom(medic, medic.Map.mapPawns.SpawnedPawnsInFaction(medic.Faction), maxDistSq, patients);
            Faction playerFaction = Faction.OfPlayerSilentFail;
            if (medic.Faction != null && playerFaction != null && medic.Faction != playerFaction)
            {
                CollectFrom(medic, medic.Map.mapPawns.SpawnedPawnsInFaction(playerFaction), maxDistSq, patients);
            }
            return patients;
        }

        private static void CollectFrom(Pawn medic, List<Pawn> pawns, float maxDistSq, List<Pawn> results)
        {
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn patient = pawns[i];
                if (patient == null || !patient.RaceProps.Humanlike) continue;
                if (patient.InAggroMentalState) continue;
                if (patient.guest?.IsPrisoner == true) continue;
                if (patient.IsMutant && !patient.mutant.Def.entitledToMedicalCare) continue;
                if (!patient.health.HasHediffsNeedingTend()) continue;
                if (medic.Position.DistanceToSquared(patient.Position) > maxDistSq) continue;
                results.Add(patient);
            }
        }

        // 与原版 ThinkNode_ConditionalNPCCanSelfTendNow 相同的本地威胁判定：
        // 邻近区域存在活跃威胁时不开始医疗，避免放下交火去包扎。
        private static bool HasNearbyActiveThreat(Pawn pawn)
        {
            bool found = false;
            RegionTraverser.BreadthFirstTraverse(pawn.Position, pawn.Map, RegionTraverser.PassAll, delegate(Region region)
            {
                List<Thing> targets = region.ListerThings.ThingsInGroup(ThingRequestGroup.AttackTarget);
                for (int i = 0; i < targets.Count; i++)
                {
                    if (GenHostility.IsActiveThreatTo((IAttackTarget)targets[i], pawn.Faction, ignoreHives: false))
                    {
                        found = true;
                        break;
                    }
                }
                return found;
            }, MaxThreatRegionDepth);
            return found;
        }

        private static Thing BestInventoryMedicine(Pawn medic, Pawn patient)
        {
            Thing best = null;
            float bestPotency = -1f;
            Faction playerFaction = Faction.OfPlayerSilentFail;
            foreach (Thing thing in medic.inventory.innerContainer)
            {
                if (!thing.def.IsMedicine) continue;
                // 玩家小人遵守其医疗护理设定；支援队内部一律允许用药。
                if (playerFaction != null && patient.Faction == playerFaction && patient.playerSettings != null
                    && !patient.playerSettings.medCare.AllowsMedicine(thing.def)) continue;
                float potency = thing.def.GetStatValueAbstract(StatDefOf.MedicalPotency);
                if (potency > bestPotency)
                {
                    bestPotency = potency;
                    best = thing;
                }
            }
            return best;
        }
    }

    // 与原版 LordToil_HuntEnemies 相同的索敌流程，仅把 duty 换成
    // Mugirl_CorporateSupportHunt，使空闲医疗节点进入思维树；
    // 撤离阶段仍使用原版 LordToil_ExitMap。
    public class LordToil_CorporateSupportHunt : LordToil_HuntEnemies
    {
        public LordToil_CorporateSupportHunt(IntVec3 fallbackLocation) : base(fallbackLocation) { }

        public override void UpdateAllDuties()
        {
            var huntData = (LordToilData_HuntEnemies)data;
            if (!huntData.fallbackLocation.IsValid)
            {
                for (int i = 0; i < lord.ownedPawns.Count; i++)
                {
                    Pawn pawn = lord.ownedPawns[i];
                    if (pawn.Spawned && RCellFinder.TryFindRandomSpotJustOutsideColony(pawn, out huntData.fallbackLocation)
                        && huntData.fallbackLocation.IsValid)
                    {
                        break;
                    }
                }
            }
            for (int j = 0; j < lord.ownedPawns.Count; j++)
            {
                Pawn pawn2 = lord.ownedPawns[j];
                pawn2.mindState.duty = new PawnDuty(MugirlContentDefOf.Mugirl_CorporateSupportHunt);
                pawn2.mindState.duty.focusSecond = huntData.fallbackLocation;
            }
        }
    }
}
