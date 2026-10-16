namespace Mugirl.Features.Roping;

// 牵引者心情（Thoughts_Traits.xml）：牵着 1 只或 2 只以上正在跟随的雪牛娘时分别进入两个阶段。
// 只统计该牵引者自己的牵引列表，不扫描地图。
public class ThoughtWorker_Roper : ThoughtWorker
{
    private const int MaxCountedFollowers = 2;

    protected override ThoughtState CurrentStateInternal(Pawn p)
    {
        if (p?.Map is null || !p.Spawned)
        {
            return ThoughtState.Inactive;
        }

        int followCount = RopingService.CountMugirlFollowers(p, MaxCountedFollowers);
        if (followCount == 0)
        {
            return ThoughtState.Inactive;
        }

        return ThoughtState.ActiveAtStage(followCount == 1 ? 0 : 1);
    }
}
