namespace Mugirl.Features.WeaponWheel;

// 抬枪/换枪动画在某一时刻的只读状态，由组件生成、渲染器消费。进度都在 [0, 1]。
internal struct WeaponWheelAnimationSnapshot
{
    internal ThingWithComps? OutgoingWeapon;
    internal ThingWithComps? IncomingWeapon;
    // 整段动画进度。
    internal float Progress;
    // 收枪（旧武器滑出）进度，时长单独配置。
    internal float OutgoingProgress;
    // 瞄准角（度，0 = 北）。
    internal float AimAngle;
    // 卡入后的闪光进度；卡入前为 0。
    internal float SnapProgress;
    // 卡入时刻在整段动画中的位置。
    internal float SnapStartProgress;
    // 换枪（true）或从收枪状态抬枪（false）。
    internal bool IsSwitching;
}
