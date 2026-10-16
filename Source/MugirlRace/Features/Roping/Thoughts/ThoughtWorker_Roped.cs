namespace Mugirl.Features.Roping;

// 被牵引心情（Thoughts_Traits.xml）：雪牛娘身体且正在跟随牵引者。
public class ThoughtWorker_Roped : ThoughtWorker
{
    protected override ThoughtState CurrentStateInternal(Pawn p)
    {
        if (p?.Map is null || !p.Spawned)
        {
            return ThoughtState.Inactive;
        }

        return RopingService.IsMugirlRopee(p) && RopingService.IsFollowingRoper(p)
            ? ThoughtState.ActiveAtStage(0)
            : ThoughtState.Inactive;
    }
}
