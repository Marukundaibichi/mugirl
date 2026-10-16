using System.Collections.Generic;
using Mugirl.Features.WeaponWheel;

namespace Mugirl.Tests.WeaponWheel;

/// <summary><see cref="WeaponWheelSlotMath"/>：槽位查找与剑舞换手后的轮转（与 1.6 的 PromoteSlotToPrimary 同一规则）。</summary>
public sealed class WeaponWheelSlotMathTests
{
    [Fact]
    public void Resize_PadsAndTruncates()
    {
        List<string?> slots = ["a"];
        WeaponWheelSlotMath.Resize(slots, 3, null);
        Assert.Equal(["a", null, null], slots);
        WeaponWheelSlotMath.Resize(slots, 2, null);
        Assert.Equal(["a", null], slots);
    }

    [Fact]
    public void CountAndFirstEmpty_SkipLockedSlots()
    {
        string?[] slots = ["a", null, "c", null, "e", null];
        bool[] unlocked = [true, false, true, true, false, true];
        Assert.Equal(2, WeaponWheelSlotMath.CountOccupiedUnlocked(slots, unlocked));
        Assert.Equal(3, WeaponWheelSlotMath.FirstUnlockedEmpty(slots, unlocked, 1));
        Assert.Equal(3, WeaponWheelSlotMath.FirstUnlockedEmpty(slots, unlocked, -4));
        Assert.Equal(5, WeaponWheelSlotMath.FirstUnlockedEmpty(slots, unlocked, 4));
        Assert.Equal(-1, WeaponWheelSlotMath.FirstUnlockedEmpty(slots, unlocked, 6));
    }

    [Fact]
    public void FindNextCyclic_WrapsAndExcludesCurrent()
    {
        string?[] slots = ["sword", "gun", "axe", null, "spear", "knife"];
        bool[] unlocked = [true, true, true, true, false, true];
        static bool Melee(string weapon) => weapon != "gun";
        Assert.Equal(2, WeaponWheelSlotMath.FindNextCyclic(slots, unlocked, 0, Melee));
        // 4 号槽已锁定，跳过。
        Assert.Equal(5, WeaponWheelSlotMath.FindNextCyclic(slots, unlocked, 2, Melee));
        // 从末尾回绕到 0 号槽。
        Assert.Equal(0, WeaponWheelSlotMath.FindNextCyclic(slots, unlocked, 5, Melee));
        Assert.Equal(-1, WeaponWheelSlotMath.FindNextCyclic(["sword", "gun"], [true, true], 0, Melee));
    }

    [Fact]
    public void TryRotateToFront_RotatesOccupiedSlotsInCycleOrder()
    {
        string?[] slots = ["a", null, "b", "c", null, "d"];
        bool[] unlocked = [true, true, true, true, true, true];
        List<int> indices = [];
        List<string?> rotated = [];
        Assert.True(WeaponWheelSlotMath.TryRotateToFront(slots, unlocked, 3, indices, rotated));
        Assert.Equal([0, 2, 3, 5], indices);
        Assert.Equal(["c", "d", "a", "b"], rotated);
    }

    [Fact]
    public void TryRotateToFront_RejectsInvalidLayouts()
    {
        List<int> indices = [];
        List<string?> rotated = [];
        Assert.False(WeaponWheelSlotMath.TryRotateToFront(["a", "b"], [true, false], 1, indices, rotated), "promoted slot is locked");
        Assert.False(WeaponWheelSlotMath.TryRotateToFront([null, "b", "c"], [true, true, true], 2, indices, rotated), "slot 0 is empty");
        Assert.False(WeaponWheelSlotMath.TryRotateToFront(["a", null], [true, true], 1, indices, rotated), "promoted slot is empty");
        Assert.False(WeaponWheelSlotMath.TryRotateToFront(["a"], [true], 0, indices, rotated), "only one weapon");
    }
}
