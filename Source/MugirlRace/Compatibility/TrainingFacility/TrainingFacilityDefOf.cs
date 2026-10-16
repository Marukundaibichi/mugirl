namespace Mugirl.Compatibility.TrainingFacility;

/// <summary>
/// Training Facility（Haplo.Miscellaneous.Training）的靶场 JobDef；未启用时由原版绑定为 null，不报错。
/// </summary>
/// <remarks>defName 按该 Mod 1.6 目录的 Defs/JobDefs/TrainingFacility_JobDefs.xml 核对（1.7 通过加载目录回退读取）。</remarks>
[DefOf]
public static class TrainingFacilityDefOf
{
    [MayRequire(ModCompat.TrainingFacilityPackageId)]
    public static JobDef? UseShootingRange;

    [MayRequire(ModCompat.TrainingFacilityPackageId)]
    public static JobDef? UseShootingRange_NonJoy;

    [MayRequire(ModCompat.TrainingFacilityPackageId)]
    public static JobDef? UseShootingRange_NonJoy_Work;

    static TrainingFacilityDefOf() => DefOfHelper.EnsureInitializedInCtor(typeof(TrainingFacilityDefOf));
}
