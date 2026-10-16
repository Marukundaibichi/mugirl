namespace Mugirl.Features.WeaponWheel;

/// <summary>一个槽位的武器引用与解锁状态；保存为一条记录，位置与解锁状态不会因并行列表长度不同而错位。</summary>
internal sealed class WeaponWheelSlot : IExposable
{
    internal ThingWithComps? Weapon;
    internal bool Unlocked;

    public WeaponWheelSlot() { }

    public void ExposeData()
    {
        Scribe_References.Look(ref Weapon, "weapon");
        Scribe_Values.Look(ref Unlocked, "unlocked", false);
    }
}
