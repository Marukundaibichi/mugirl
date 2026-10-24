using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Mugirl.Features.Mounting;
using Mugirl.Features.WeaponWheel;
using Verse;
using Verse.AI;

namespace Mugirl.Tests.WeaponWheel;

/// <summary>
/// 轮盘的逐 tick 调度：原版持械判定（PawnRenderUtility.CarryWeaponOpenly）每 tick 只问一次，坐骑切换、亮枪状态与空闲时的背负显示共用；
/// 非空闲或骑乘降级时背负显示照常现算。真实组件挂在最小 Pawn 上，由 CompTick 驱动；不在游戏中，tick 恒为 0。
/// 这个 Pawn 没有搬运、征召、Duty 与 Lord，持械判定必然问到组件，所以由组件的 WantHoldWeapon 计数并控制结果。
/// </summary>
[Collection("Combat hooks")]
public sealed class WeaponWheelTickTests
{
    [Fact]
    public void IdleTicksAskTheHoldCheckOnceAndShowThePrimaryOnTheBack()
    {
        var rig = new Rig(withMount: false);
        Assert.True(rig.Wheel.combat.IsIdle);
        Assert.True(rig.Wheel.swordDance.IsIdle);

        rig.Wheel.CompTick();
        rig.Wheel.CompTick();

        Assert.Equal(2, rig.Probe.Calls);
        Assert.Equal(WeaponWheelCombatState.Holstered, rig.Wheel.combat.combatState);
        Assert.False(rig.Wheel.combat.wasWeaponOpenlyHeld);
        // 未亮枪：第 1 格的主武器显示在背上。
        Assert.Equal(1, rig.Wheel.renderCache.backWeaponVisibilityMask);
    }

    [Fact]
    public void HoldingTheWeaponStartsTheEquipAnimationAndHidesTheBackCopy()
    {
        var rig = new Rig(withMount: false);
        rig.Probe.Hold = true;
        rig.Wheel.CompTick();

        Assert.Equal(1, rig.Probe.Calls);
        Assert.Equal(WeaponWheelCombatState.EquippingPrimary, rig.Wheel.combat.combatState);
        Assert.True(rig.Wheel.combat.wasWeaponOpenlyHeld);
        Assert.False(rig.Wheel.combat.IsIdle);
        // 抬枪动画接管主武器：背上不再显示，背负显示也不必再问持械判定。
        Assert.Equal(0, rig.Wheel.renderCache.backWeaponVisibilityMask);

        rig.Probe.Hold = false;
        rig.Wheel.CompTick();

        Assert.Equal(2, rig.Probe.Calls);
        Assert.False(rig.Wheel.combat.wasWeaponOpenlyHeld);
        Assert.Equal(WeaponWheelCombatState.EquippingPrimary, rig.Wheel.combat.combatState);
        Assert.Equal(0, rig.Wheel.renderCache.backWeaponVisibilityMask);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MountedTicksLeaveTheHoldCheckToTheBackDisplayAndDismountSharesOneAnswer(bool holdOnDismount)
    {
        var rig = new Rig(withMount: true);
        rig.MountRider();
        rig.Wheel.CompTick();

        // 骑乘降级：亮枪状态不推进，只有背负显示问一次。
        Assert.Equal(1, rig.Probe.Calls);
        Assert.Equal(WeaponWheelCombatState.DisabledMounted, rig.Wheel.combat.combatState);
        Assert.Equal(1, rig.Wheel.renderCache.backWeaponVisibilityMask);

        rig.Dismount();
        rig.Probe.Hold = holdOnDismount;
        rig.Wheel.CompTick();

        // 下坐骑这一 tick：坐骑切换、亮枪状态与背负显示共用同一次判定。
        Assert.Equal(2, rig.Probe.Calls);
        Assert.Equal(holdOnDismount ? WeaponWheelCombatState.Ready : WeaponWheelCombatState.Holstered, rig.Wheel.combat.combatState);
        Assert.Equal(holdOnDismount, rig.Wheel.combat.wasWeaponOpenlyHeld);
        Assert.Equal(holdOnDismount ? 0 : 1, rig.Wheel.renderCache.backWeaponVisibilityMask);
    }

    private static ThingDef Definition(string name)
    {
        var definition = (ThingDef)RuntimeHelpers.GetUninitializedObject(typeof(ThingDef));
        definition.defName = name;
        return definition;
    }

    private static Pawn NewPawn(string name, int id) => new() { def = Definition(name), thingIDNumber = id };

    // 雪牛娘：主武器在第 1 格且由原版装备栏持有，正在做既不强制亮枪也不隐藏武器的普通工作。
    private sealed class Rig
    {
        private readonly CompMount? mount;
        private readonly Pawn rider = NewPawn("WheelTickRider", 8);

        internal Rig(bool withMount)
        {
            // thingIDNumber 决定维护周期的错开量：7 使 tick 0 不跑维护。
            Pawn pawn = NewPawn("WheelTickPawn", 7);
            // 未初始化的 healthState 是枚举首项 Dead，只把它设为 Mobile。
            var health = (Pawn_HealthTracker)RuntimeHelpers.GetUninitializedObject(typeof(Pawn_HealthTracker));
            typeof(Pawn_HealthTracker).GetField("healthState", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(health, PawnHealthState.Mobile);
            pawn.health = health;

            ThingDef weaponDef = Definition("WheelTickWeapon");
            weaponDef.equipmentType = EquipmentType.Primary;
            var weapon = new ThingWithComps { def = weaponDef };
            pawn.equipment = new Pawn_EquipmentTracker(pawn);
            ((ThingOwner<ThingWithComps>)pawn.equipment.GetDirectlyHeldThings()).InnerListForReading.Add(weapon);
            pawn.jobs = new Pawn_JobTracker(pawn) { curJob = new Job { def = new JobDef { defName = "WheelTickJob" } } };

            Probe.parent = pawn;
            List<ThingComp> comps = [Probe];
            if (withMount)
            {
                mount = new CompMount { parent = pawn };
                comps.Add(mount);
            }

            typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pawn, comps);
            Wheel = new CompWeaponWheel { parent = pawn };
            Wheel.Initialize(new CompProperties_WeaponWheel());
            Wheel.storage.slots[0].Weapon = weapon;
        }

        internal CompWeaponWheel Wheel { get; }

        internal HoldProbe Probe { get; } = new();

        // 本 Pawn 作为坐骑驮着骑手。
        internal void MountRider() => Riders.InnerListForReading.Add(rider);

        internal void Dismount()
        {
            Riders.InnerListForReading.Clear();
            // tick 恒为 0：清掉按 tick 缓存的骑乘判定，相当于进入下一 tick。
            Wheel.combat.mountStateCacheTick = int.MinValue;
        }

        private ThingOwner<Thing> Riders => (ThingOwner<Thing>)mount!.GetDirectlyHeldThings();
    }

    private sealed class HoldProbe : ThingComp
    {
        internal int Calls { get; private set; }

        internal bool Hold { get; set; }

        public override bool WantHoldWeapon(Pawn pawn)
        {
            Calls++;
            return Hold;
        }
    }
}
