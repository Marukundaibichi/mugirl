using Mugirl.Features.WeaponWheel;

namespace Mugirl.Compatibility.DualWield;

/// <summary>一次主武器切换中暂时收起的副手；Postfix 与 Finalizer 共用，保证只归还一次。</summary>
internal sealed class DualWieldOffHandTransferState
{
    private readonly CompWeaponWheel wheel;
    private readonly ThingWithComps offHand;
    private bool restored;

    internal DualWieldOffHandTransferState(CompWeaponWheel wheel, ThingWithComps offHand)
    {
        this.wheel = wheel;
        this.offHand = offHand;
    }

    internal void Restore()
    {
        if (restored)
        {
            return;
        }

        restored = true;
        DualWieldCompatibility.RestoreOffHand(wheel, offHand);
    }
}
