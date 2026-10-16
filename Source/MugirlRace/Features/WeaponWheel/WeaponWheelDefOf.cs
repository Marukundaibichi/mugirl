namespace Mugirl.Features.WeaponWheel;

// 武器轮盘功能的 Def。
[DefOf]
internal static class WeaponWheelDefOf
{
    public static JobDef Mugirl_LoadWeaponWheel = null!;

    public static ResearchProjectDef Mugirl_Autoloading = null!;

    public static HediffDef Mugirl_LinkedSwordDance = null!;

    static WeaponWheelDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(WeaponWheelDefOf));
}
