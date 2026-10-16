using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Mounting;

// 骑乘关系查询（坐骑组件、骑手所在坐骑、是否在骑乘）与骑乘绘制距离。
internal static class MountUtility
{
    internal static CompMount? GetMountComp(Pawn? pawn)
    {
        return pawn?.TryGetComp<CompMount>();
    }

    internal static CompMount? GetMountForRider(Pawn? rider)
    {
        if (rider is null)
        {
            return null;
        }

        return rider.ParentHolder as CompMount ?? ThingOwnerUtility.GetAnyParent<CompMount>(rider);
    }

    internal static bool IsMounted([NotNullWhen(true)] Pawn? pawn, [NotNullWhen(true)] out CompMount? comp)
    {
        comp = GetMountForRider(pawn);
        if (comp is not null && pawn is not null && comp.MountedPawn == pawn)
        {
            return true;
        }

        return false;
    }

    internal static float EquipmentDrawDistanceFactor(Pawn? pawn)
    {
        return pawn?.ageTracker?.CurLifeStage?.equipmentDrawDistanceFactor ?? 1f;
    }
}
