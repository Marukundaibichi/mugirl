using Mugirl.Core;
using Mugirl.Features.WeaponWheel;
using Verse;

namespace Mugirl.Tests.WeaponWheel;

/// <summary>同步作用域（预热跳过、装备转移）与状态文字的选择规则。</summary>
public sealed class WeaponWheelScopeTests
{
    [Fact]
    public void WarmupScope_AppliesOnlyToItsVerbAndRestores()
    {
        StubVerb first = new();
        StubVerb second = new();
        Assert.False(WeaponWheelWarmupScope.IsSkipping(first));
        using (new WeaponWheelWarmupScope(first))
        {
            Assert.True(WeaponWheelWarmupScope.IsSkipping(first));
            Assert.False(WeaponWheelWarmupScope.IsSkipping(second));
            Assert.False(WeaponWheelWarmupScope.IsSkipping(null));
            using (new WeaponWheelWarmupScope(null))
            {
                Assert.True(WeaponWheelWarmupScope.IsSkipping(first));
            }
            using (new WeaponWheelWarmupScope(first))
            {
                Assert.True(WeaponWheelWarmupScope.IsSkipping(first));
            }
            Assert.True(WeaponWheelWarmupScope.IsSkipping(first));
        }
        Assert.False(WeaponWheelWarmupScope.IsSkipping(first));
    }

    [Fact]
    public void TransferScope_NestsAndRestores()
    {
        Assert.False(EquipmentTransferScope.IsWheelOperation);
        using (EquipmentTransferScope.Registration())
        {
            Assert.True(EquipmentTransferScope.IsWheelOperation);
            Assert.False(EquipmentTransferScope.IsInternalTransfer);
            using (EquipmentTransferScope.InternalTransfer())
            {
                Assert.True(EquipmentTransferScope.IsInternalTransfer);
            }
            Assert.False(EquipmentTransferScope.IsInternalTransfer);
            Assert.True(EquipmentTransferScope.IsWheelOperation);
        }
        Assert.False(EquipmentTransferScope.IsWheelOperation);
    }

    [Fact]
    public void Status_AnyLinkedModeCoversBothModes()
    {
        Assert.True(new WeaponWheelStatus(SwordDance: true, FullFirepower: false).AnyLinkedMode);
        Assert.True(new WeaponWheelStatus(SwordDance: false, FullFirepower: true).AnyLinkedMode);
        Assert.False(default(WeaponWheelStatus).AnyLinkedMode);
    }

    private sealed class StubVerb : Verb
    {
        protected override bool TryCastShot() => false;
    }
}
