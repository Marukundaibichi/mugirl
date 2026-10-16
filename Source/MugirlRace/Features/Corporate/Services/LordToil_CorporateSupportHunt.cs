using Verse.AI;
using Verse.AI.Group;

namespace Mugirl.Features.Corporate;

// 与原版 LordToil_HuntEnemies 相同的索敌流程，仅把 duty 换成 Mugirl_CorporateSupportHunt，
// 使空闲医疗节点进入思维树；撤离阶段仍使用原版 LordToil_ExitMap。
public class LordToil_CorporateSupportHunt : LordToil_HuntEnemies
{
    public LordToil_CorporateSupportHunt(IntVec3 fallbackLocation)
        : base(fallbackLocation)
    {
    }

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
            Pawn member = lord.ownedPawns[j];
            member.mindState.duty = new PawnDuty(CorporateDefOf.Mugirl_CorporateSupportHunt);
            member.mindState.duty.focusSecond = huntData.fallbackLocation;
        }
    }
}
