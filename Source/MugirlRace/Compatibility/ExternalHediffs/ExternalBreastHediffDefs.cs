namespace Mugirl.Compatibility.ExternalHediffs;

/// <summary>
/// 其他身体类 Mod 的乳房 HediffDef；对应 Mod 未加载时为 null。
/// </summary>
/// <remarks>
/// 来源 Mod 的 packageId 未经核对，无法写成带 <c>[MayRequire]</c> 的 DefOf；未声明的 DefOf 字段在缺失时会记录错误。
/// 所以仍按 defName 静默查找，并且只在 Def 加载完成后查一次：<c>[StaticConstructorOnStartup]</c> 让原版在启动阶段运行静态构造函数，
/// 显式静态构造函数让类型不是 beforefieldinit，不会在 JIT 编译读取方时被提前初始化而把 null 永久缓存下来。
/// 这些 defName 字面量只允许出现在本类型中（验证程序 external-breast-hediffs 规则）。
/// </remarks>
[StaticConstructorOnStartup]
internal static class ExternalBreastHediffDefs
{
    // StaticCacheLifecycle: 进程级；Def 加载完成后解析一次，Def 数据库在进程内不再变化，无需清理。
    internal static readonly HediffDef? HugeBreasts;
    internal static readonly HediffDef? BionicBreasts;
    internal static readonly HediffDef? SlimeBreasts;
    internal static readonly HediffDef? GrMuffaloMammaries;
    internal static readonly HediffDef? Breasts;
    internal static readonly HediffDef? HydraulicBreasts;
    internal static readonly HediffDef? SmallBreasts;
    internal static readonly HediffDef? LargeBreasts;
    internal static readonly HediffDef? ArchotechBreasts;
    internal static readonly HediffDef? FlatBreasts;

    static ExternalBreastHediffDefs()
    {
        HugeBreasts = Get("HugeBreasts");
        BionicBreasts = Get("BionicBreasts");
        SlimeBreasts = Get("SlimeBreasts");
        GrMuffaloMammaries = Get("GR_MuffaloMammaries");
        Breasts = Get("Breasts");
        HydraulicBreasts = Get("HydraulicBreasts");
        SmallBreasts = Get("SmallBreasts");
        LargeBreasts = Get("LargeBreasts");
        ArchotechBreasts = Get("ArchotechBreasts");
        FlatBreasts = Get("FlatBreasts");
    }

    private static HediffDef? Get(string defName) => DefDatabase<HediffDef>.GetNamedSilentFail(defName);
}
