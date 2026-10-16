namespace Mugirl.Features.WeaponWheel;

/// <summary>切换主手前的临时装备转移；token 非空时必须恢复一次，返回 false 时取消切换。</summary>
internal interface IWeaponWheelPrimarySwapGuard
{
    bool TryBeginPrimarySwap(CompWeaponWheel wheel, out object? token);
    void EndPrimarySwap(CompWeaponWheel wheel, object token);
}
