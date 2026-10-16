namespace Mugirl.Features.Social;

/// <summary>雪牛娘主武器有单发动词时生效（XML workerClass）。</summary>
public class ThoughtWorker_SingleshotWeaponAversion : ThoughtWorker
{
    protected override ThoughtState CurrentStateInternal(Pawn p)
    {
        if (!MugirlIdentity.IsMugirlPawn(p))
        {
            return false;
        }

        List<VerbProperties>? verbs = p.equipment?.Primary?.def?.Verbs;
        if (verbs is null)
        {
            return false;
        }

        for (int i = 0; i < verbs.Count; i++)
        {
            if (verbs[i].burstShotCount == 1)
            {
                return true;
            }
        }

        return false;
    }
}
