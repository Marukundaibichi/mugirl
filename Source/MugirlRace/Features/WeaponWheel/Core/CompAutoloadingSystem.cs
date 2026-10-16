namespace Mugirl.Features.WeaponWheel;

// 自驱装弹系统服装的组件，由 CompProperties_AutoloadingSystem 指定。
// 穿上或脱下时通知穿戴者的武器轮盘重算“是否穿戴装弹系统”。
public sealed class CompAutoloadingSystem : ThingComp
{
    public bool SkipCycleReload => ((CompProperties_AutoloadingSystem)props).skipCycleReload;

    public override void Notify_Equipped(Pawn pawn)
    {
        base.Notify_Equipped(pawn);
        pawn?.GetComp<CompWeaponWheel>()?.NotifyAutoloadingApparelChanged();
    }

    public override void Notify_Unequipped(Pawn pawn)
    {
        base.Notify_Unequipped(pawn);
        pawn?.GetComp<CompWeaponWheel>()?.NotifyAutoloadingApparelChanged();
    }
}
