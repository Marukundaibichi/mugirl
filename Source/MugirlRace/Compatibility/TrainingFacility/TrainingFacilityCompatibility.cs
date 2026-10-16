namespace Mugirl.Compatibility.TrainingFacility;

/// <summary>Training Facility：Pawn 在靶场练习时，轮盘不接管施放。</summary>
internal static class TrainingFacilityCompatibility
{
    internal static bool Active => ModCompat.TrainingFacility;

    internal static void RegisterHooks()
    {
        if (Active)
        {
            Mugirl.Features.WeaponWheel.WeaponWheelHooks.Current.AddCombatBypass("TrainingFacility", ShouldBypassWeaponWheelCombat);
        }
    }

    /// <remarks>每次轮盘施放决策调用三次；1.6 每次都查 Mod 列表并比较 defName，现在只比较缓存的开关与 Def 引用。</remarks>
    internal static bool ShouldBypassWeaponWheelCombat(Pawn? pawn)
    {
        if (!Active)
        {
            return false;
        }

        JobDef? job = pawn?.CurJobDef;
        return job is not null
            && (job == TrainingFacilityDefOf.UseShootingRange
                || job == TrainingFacilityDefOf.UseShootingRange_NonJoy
                || job == TrainingFacilityDefOf.UseShootingRange_NonJoy_Work);
    }
}
