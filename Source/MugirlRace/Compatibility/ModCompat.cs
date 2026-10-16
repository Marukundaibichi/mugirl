namespace Mugirl.Compatibility;

/// <summary>
/// 兼容层识别的第三方 Mod：packageId 常量与启用状态。
/// </summary>
/// <remarks>
/// <c>ModLister.GetActiveModWithIdentifier</c> 会执行小写化、Trim 与字典查找，并可能分配字符串，
/// 其中训练设施的判断在每次轮盘施放决策中调用三次。已启用的 Mod 列表在一次运行中不变（改动需要重启游戏），
/// 这里首次访问时一次算出全部结果，热路径直接读取。
/// </remarks>
internal static class ModCompat
{
    internal const string DualWieldPackageId = "MemeGoddess.DualWield";
    internal const string LactationExpansionPackageId = "Euterpe.MilkyTitfuck";
    internal const string MeleeAnimationPackageId = "co.uk.epicguru.meleeanimation";
    internal const string RunAndGunContinuedPackageId = "MemeGoddess.RunAndGun";
    internal const string RunAndGunLegacyPackageId = "roolo.RunAndGun.kotobike";
    internal const string TrainingFacilityPackageId = "Haplo.Miscellaneous.Training";
    internal const string YayoAnimationPackageId = "com.yayo.yayoAni.continued";
    internal const string YayoCombatPackageId = "Mlie.YayosCombat3";

    /// <summary>Harmony id，供 <see cref="HarmonyLib.HarmonyBefore"/>/<see cref="HarmonyLib.HarmonyAfter"/> 使用。</summary>
    internal const string LactationExpansionHarmonyId = "EuterpeMilkyTitfuck.LactationExpansion";

    internal const string YayoCombatHarmonyId = "Mlie.YayosCombat3";

    internal static bool DualWield => Flags.DualWield;

    internal static bool LactationExpansion => Flags.LactationExpansion;

    internal static bool MeleeAnimation => Flags.MeleeAnimation;

    /// <summary>RunAndGun 的旧版或续作任一启用。</summary>
    internal static bool RunAndGun => Flags.RunAndGun;

    internal static bool TrainingFacility => Flags.TrainingFacility;

    internal static bool YayoAnimation => Flags.YayoAnimation;

    internal static bool YayoCombat => Flags.YayoCombat;

    private static bool IsActive(string packageId) => ModLister.GetActiveModWithIdentifier(packageId) is not null;

    // 显式静态构造函数：类型不是 beforefieldinit，只在第一次读取时运行（Mod 构造函数中的 Prepare 已在 Mod 列表加载之后）。
    private static class Flags
    {
        // StaticCacheLifecycle: 进程级不可变；Mod 列表只在重启后变化，无需清理。
        internal static readonly bool DualWield;
        internal static readonly bool LactationExpansion;
        internal static readonly bool MeleeAnimation;
        internal static readonly bool RunAndGun;
        internal static readonly bool TrainingFacility;
        internal static readonly bool YayoAnimation;
        internal static readonly bool YayoCombat;

        static Flags()
        {
            DualWield = IsActive(DualWieldPackageId);
            LactationExpansion = IsActive(LactationExpansionPackageId);
            MeleeAnimation = IsActive(MeleeAnimationPackageId);
            RunAndGun = IsActive(RunAndGunLegacyPackageId) || IsActive(RunAndGunContinuedPackageId);
            TrainingFacility = IsActive(TrainingFacilityPackageId);
            YayoAnimation = IsActive(YayoAnimationPackageId);
            YayoCombat = IsActive(YayoCombatPackageId);
        }
    }
}
