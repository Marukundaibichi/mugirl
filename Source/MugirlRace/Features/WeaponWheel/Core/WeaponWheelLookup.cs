namespace Mugirl.Features.WeaponWheel;

// 补丁入口共用的组件查找（Compatibility/WeaponWheel 与 MeleeAnimation 兼容也调用）。
internal static class WeaponWheelLookup
{
    // 先用种族字段比较挡掉非雪牛娘；CompWeaponWheel 是 sealed，GetComp 直接命中 compsByType 字典，
    // 无锁、可在渲染工作线程调用（取代 1.6 的 ConditionalWeakTable 缓存）。
    internal static CompWeaponWheel? CompFor(Pawn? pawn)
    {
        return MugirlIdentity.IsMugirlPawn(pawn) ? pawn.GetComp<CompWeaponWheel>() : null;
    }
}
