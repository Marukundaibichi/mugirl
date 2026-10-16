namespace Mugirl.Features.WeaponWheel;

// 轮盘战斗状态，只在运行期使用（不存档；读档后 ResetRuntimeState 回到 Holstered）。
internal enum WeaponWheelCombatState
{
    Holstered,
    EquippingPrimary,
    Ready,
    Firing,
    Switching,
    SwordDanceSwitching,
    CycleCooldown,
    DisabledMounted
}
