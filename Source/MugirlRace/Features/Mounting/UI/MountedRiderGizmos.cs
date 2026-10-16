namespace Mugirl.Features.Mounting;

// 骑乘中的骑手只显示“选择载具”和“下骑”两个命令（Harmony_Pawn_GetGizmos_Rider 替换原列表）。
// 动作执行时重新查询骑乘关系，Gizmo 缓存期间载具变化也能作用到当前载具。
internal static class MountedRiderGizmos
{
    internal static IEnumerable<Gizmo> For(Pawn rider, CompMount comp)
    {
        if (comp.MooPawn is not null)
        {
            yield return new Command_Action
            {
                defaultLabel = "Mugirl.Mount.SelectCarrier".Translate(),
                defaultDesc = "Mugirl.Mount.SelectCarrierDesc".Translate(),
                icon = TexCommand.SelectCarriedThing,
                action = () => SelectionUtility.SelectInPlaying(CurrentMount(rider, comp).MooPawn),
            };
        }

        yield return new Command_Action
        {
            defaultLabel = "Mugirl.Mount.DismountRider".Translate(),
            defaultDesc = "Mugirl.Mount.DismountRiderDesc".Translate(),
            icon = TexCommand.DropCarriedPawn,
            action = () => CurrentMount(rider, comp).TryDismount(),
        };
    }

    private static CompMount CurrentMount(Pawn rider, CompMount fallback)
    {
        return MountUtility.GetMountForRider(rider) ?? fallback;
    }
}
