using System.Diagnostics.CodeAnalysis;
using Verse.AI;

namespace Mugirl.Features.Social;

/// <summary>在同房间、同派系、40 格内的人形同伴中均匀随机挑一个去蹭蹭（XML ThinkNode）。</summary>
public class JobGiver_MugirlNuzzle : ThinkNode_JobGiver
{
    private const float MaxNuzzleDistance = 40f;
    private const int JobExpiryTicks = 3000;

    protected override Job? TryGiveJob(Pawn pawn)
    {
        if (pawn.Map is null || NuzzleUtility.GetNuzzleMTBHours(pawn) <= 0f)
        {
            return null;
        }

        if (!TryFindNuzzleTarget(pawn, out Pawn? target))
        {
            return null;
        }

        Job job = JobMaker.MakeJob(SocialDefOf.Mugirl_Nuzzle, target);
        // 使用正常移动速度接近目标，避免散步限速导致追不上移动中的互动对象。
        job.locomotionUrgency = LocomotionUrgency.Jog;
        job.expiryInterval = JobExpiryTicks;
        return job;
    }

    private static bool TryFindNuzzleTarget(Pawn pawn, [NotNullWhen(true)] out Pawn? target)
    {
        target = null;
        int candidateCount = 0;
        Room? pawnRoom = pawn.GetRoom(RegionType.Set_All);
        List<Pawn> spawnedPawns = pawn.Map.mapPawns.SpawnedPawnsInFaction(pawn.Faction);

        // 蓄水池抽样：一次遍历即可在全部有效候选中等概率选一个，不分配候选列表。
        for (int i = 0; i < spawnedPawns.Count; i++)
        {
            Pawn candidate = spawnedPawns[i];
            if (!IsValidNuzzleTarget(pawn, candidate, pawnRoom))
            {
                continue;
            }

            candidateCount++;
            if (Rand.RangeInclusive(1, candidateCount) == 1)
            {
                target = candidate;
            }
        }

        return target is not null;
    }

    private static bool IsValidNuzzleTarget(Pawn pawn, Pawn candidate, Room? pawnRoom)
    {
        return candidate != pawn
            && !candidate.NonHumanlikeOrWildMan()
            && candidate.Position.InHorDistOf(pawn.Position, MaxNuzzleDistance)
            && pawnRoom == candidate.GetRoom(RegionType.Set_All)
            && !candidate.Position.IsForbidden(pawn)
            && pawn.CanReserve(candidate)
            && candidate.CanCasuallyInteractNow(false, false, false, false);
    }
}
