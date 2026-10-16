namespace Mugirl.Features.WeaponWheel;

// 种族定义中的轮盘配置；字段由 XML 读取，运行期统一通过组件的 Props 访问。
public sealed class CompProperties_WeaponWheel : CompProperties
{
    public int maxSlots = 6;
    public int baseUnlockedSlots = 3;
    public int equipAnimationTicks = 42;
    public int outgoingAnimationTicks = 18;
    public int snapTick = 38;
    public int switchSnapTick = 30;
    public float swordDanceChance = 0.33f;
    public int swordDanceStunTicks = 60;
    public int swordDanceHasteTicks = 300;
    public int swordDanceDashAnimationTicks = 42;
    public float swordDanceRamDamage = 10f;
    public float swordDanceRamArmorPenetration = 0.2f;

    public CompProperties_WeaponWheel()
    {
        compClass = typeof(CompWeaponWheel);
    }

    // 运行期代码仍对这些值做防御性钳制；这里只在加载时提示配置错误。
    public override IEnumerable<string> ConfigErrors(ThingDef parentDef)
    {
        foreach (string error in base.ConfigErrors(parentDef))
        {
            yield return error;
        }
        if (maxSlots < 1)
        {
            yield return "maxSlots must be at least 1";
        }
        if (baseUnlockedSlots < 0 || baseUnlockedSlots > maxSlots)
        {
            yield return "baseUnlockedSlots must be between 0 and maxSlots";
        }
        if (equipAnimationTicks < 1 || outgoingAnimationTicks < 1 || swordDanceDashAnimationTicks < 1)
        {
            yield return "animation tick counts must be at least 1";
        }
        if (snapTick < 0 || snapTick > equipAnimationTicks || switchSnapTick < 0 || switchSnapTick > equipAnimationTicks)
        {
            yield return "snapTick and switchSnapTick must be between 0 and equipAnimationTicks";
        }
        if (!(swordDanceChance >= 0f && swordDanceChance <= 1f))
        {
            yield return "swordDanceChance must be between 0 and 1";
        }
        if (swordDanceRamDamage < 0f || swordDanceRamArmorPenetration < 0f)
        {
            yield return "swordDanceRamDamage and swordDanceRamArmorPenetration must not be negative";
        }
    }
}
