using Verse.AI;

namespace Mugirl.Features.Corporate;

// 支援小队空闲医疗：索敌节点拿不到目标且本地安全时，为自己或附近受伤的
// 友方小人（队员、玩家殖民者与玩家派系奴隶）处理伤口。医药只从自己的背包取，
// 不到玩家库存或地图上拿药。类名由 1.7/Defs/Corporate/CorporateSupportDuty.xml 引用。
public class JobGiver_CorporateSupportTend : ThinkNode_JobGiver
{
    private const float MaxPatientRadius = 45f;
    private const int MaxThreatRegionDepth = 5;

    protected override Job? TryGiveJob(Pawn pawn)
    {
        if (pawn == null || !pawn.Spawned || pawn.Map == null || !pawn.RaceProps.Humanlike
            || !pawn.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation))
        {
            return null;
        }

        List<Pawn> patients = CollectPatients(pawn);
        if (patients.Count == 0 || HasNearbyActiveThreat(pawn))
        {
            return null;
        }

        patients.SortBy(p => pawn.Position.DistanceToSquared(p.Position));
        for (int i = 0; i < patients.Count; i++)
        {
            Pawn patient = patients[i];
            if (!pawn.CanReach(patient, PathEndMode.ClosestTouch, Danger.Deadly) || !pawn.CanReserve(patient, 1, -1))
            {
                continue;
            }

            Thing? medicine = BestInventoryMedicine(pawn, patient);
            Job job = medicine != null
                ? JobMaker.MakeJob(CorporateDefOf.Mugirl_CorporateSupportTend, patient, medicine)
                : JobMaker.MakeJob(CorporateDefOf.Mugirl_CorporateSupportTend, patient);
            // 单次处理后结束，避免原版 FindMoreMedicineToil 把 NPC 引向地图上的玩家医药库存。
            job.endAfterTendedOnce = true;
            job.expiryInterval = GenDate.TicksPerHour;
            return job;
        }

        return null;
    }

    private static List<Pawn> CollectPatients(Pawn medic)
    {
        List<Pawn> patients = new List<Pawn>();
        float maxDistSq = MaxPatientRadius * MaxPatientRadius;
        CollectFrom(medic, medic.Map.mapPawns.SpawnedPawnsInFaction(medic.Faction), maxDistSq, patients);
        Faction? playerFaction = Faction.OfPlayerSilentFail;
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
            if (!CorporateSupportUtility.CanAcceptFieldTending(medic, patient) || !patient.RaceProps.Humanlike
                || patient.InAggroMentalState || patient.guest?.IsPrisoner == true
                || (patient.IsMutant && !patient.mutant.Def.entitledToMedicalCare)
                || !patient.health.HasHediffsNeedingTend()
                || medic.Position.DistanceToSquared(patient.Position) > maxDistSq)
            {
                continue;
            }

            results.Add(patient);
        }
    }

    // 与原版 ThinkNode_ConditionalNPCCanSelfTendNow 相同的本地威胁判定：
    // 邻近区域存在活跃威胁时不开始医疗，避免放下交火去包扎。
    private static bool HasNearbyActiveThreat(Pawn pawn)
    {
        bool found = false;
        RegionTraverser.BreadthFirstTraverse(pawn.Position, pawn.Map, RegionTraverser.PassAll, delegate (Region region)
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

    private static Thing? BestInventoryMedicine(Pawn medic, Pawn patient)
    {
        Thing? best = null;
        float bestPotency = -1f;
        Faction? playerFaction = Faction.OfPlayerSilentFail;
        foreach (Thing thing in medic.inventory.innerContainer)
        {
            if (!thing.def.IsMedicine)
            {
                continue;
            }

            // 玩家小人遵守其医疗护理设定；支援队内部一律允许用药。
            if (playerFaction != null && patient.Faction == playerFaction && patient.playerSettings != null
                && !patient.playerSettings.medCare.AllowsMedicine(thing.def))
            {
                continue;
            }

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
