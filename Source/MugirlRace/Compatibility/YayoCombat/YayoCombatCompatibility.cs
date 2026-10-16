using HarmonyLib;
using Mugirl.Features.WeaponWheel;

namespace Mugirl.Compatibility.YayoCombat;

/// <summary>
/// Yayo's Combat（Mlie.YayosCombat3）：备用武器自动装填、生成武器的初始弹药，以及让装填逻辑找到轮盘中的武器。
/// </summary>
/// <remarks>
/// 成员按 1.6 版 yayoCombat.dll 核对：<c>yayoCombat.YayoCombatCore.ammo</c>（public static bool）、
/// <c>s_enemyAmmo</c>（public static float）与 <c>yayoCombat.reloadUtility.TryAutoReload(CompApparelReloadable)</c>（public static void）。
/// </remarks>
internal static class YayoCombatCompatibility
{
    private const int AutoReloadInterval = 60;

    // StaticCacheLifecycle: 进程级失败闩；对方方法抛出一次后本进程不再调用，与 1.6 相同，无需清理。
    private static bool reloadInvocationFailed;
    private static bool generatedAmmoInitializationFailed;

    internal static bool Active => ModCompat.YayoCombat;

    internal static void RegisterHooks()
    {
        if (Active)
        {
            WeaponWheelHooks.Current.AddTickCompleted("YayoCombat", TryAutoReloadReserveWeapon);
            WeaponWheelHooks.Current.AddGeneratedWeaponInitializer("YayoCombat", InitializeGeneratedWeaponAmmo);
        }
    }

    internal static bool AmmoEnabled => ReflectionCache.TryAutoReload is not null
        && ReflectionCache.AmmoEnabled is { } ammoEnabled
        && ammoEnabled();

    /// <summary>轮盘正常完成 tick 后调用；先做最便宜的间隔判断，59/60 的 tick 直接返回。</summary>
    internal static void TryAutoReloadReserveWeapon(CompWeaponWheel wheel)
    {
        Pawn? pawn = wheel.Pawn;
        if (pawn is null || !Gen.IsHashIntervalTick(pawn, AutoReloadInterval) || reloadInvocationFailed
            || !pawn.Drafted || wheel.IsBusy
            || (pawn.CurJobDef != JobDefOf.Wait_Combat && pawn.CurJobDef != JobDefOf.AttackStatic)
            || !AmmoEnabled || ReflectionCache.TryAutoReload is not { } tryAutoReload)
        {
            return;
        }

        CompApparelReloadable? primaryReloadable = pawn.equipment?.Primary?.TryGetComp<CompApparelReloadable>();
        if (primaryReloadable?.NeedsReload(false) == true)
        {
            return;
        }

        ThingOwner<ThingWithComps> reserveWeapons = wheel.ReserveWeapons;
        for (int i = 0; i < reserveWeapons.Count; i++)
        {
            CompApparelReloadable? reloadable = reserveWeapons[i]?.TryGetComp<CompApparelReloadable>();
            if (reloadable?.NeedsReload(false) != true)
            {
                continue;
            }

            try
            {
                tryAutoReload(reloadable);
            }
            catch (Exception exception)
            {
                reloadInvocationFailed = true;
                MugirlLog.WarningOnce(
                    "WeaponWheel.YayoCombatAutoReloadFailed",
                    () => "Weapon wheel could not invoke Yayo's Combat reserve-weapon reload support: "
                        + exception.GetType().Name + ": " + exception.Message);
            }

            return;
        }
    }

    internal static void InitializeGeneratedWeaponAmmo(Pawn? pawn, ThingWithComps? weapon)
    {
        if (pawn is null || weapon is null || generatedAmmoInitializationFailed || !AmmoEnabled
            || ReflectionCache.EnemyAmmoFactor is not { } enemyAmmoFactor
            || ReflectionCache.RemainingCharges is not { } remainingCharges)
        {
            return;
        }

        CompApparelReloadable? reloadable = weapon.TryGetComp<CompApparelReloadable>();
        if (reloadable is null)
        {
            return;
        }

        try
        {
            float ammoFactor = enemyAmmoFactor();
            int maxCharges = reloadable.MaxCharges;
            float randomFactor = Rand.Range(0.7f, 1.3f);
            bool capAtMaxCharges = ammoFactor <= 1f || pawn.Faction?.IsPlayer == true;
            remainingCharges(reloadable) = GeneratedCharges(maxCharges, ammoFactor, randomFactor, capAtMaxCharges);
        }
        catch (Exception exception)
        {
            generatedAmmoInitializationFailed = true;
            MugirlLog.WarningOnce(
                "WeaponWheel.YayoCombatGeneratedAmmoFailed",
                () => "Weapon wheel could not initialize Yayo's Combat ammo for a generated reserve weapon: "
                    + exception.GetType().Name + ": " + exception.Message);
        }
    }

    /// <summary>
    /// 生成武器的初始弹药：<c>round(max × factor × random)</c>，玩家方或倍率不超过 1 时不超过上限，结果不小于 0。
    /// </summary>
    /// <remarks>
    /// 倍率来自对方的设置，保留 1.6 <c>Mathf.RoundToInt</c> 的 Mono 规则：NaN 或越界得 <c>int.MinValue</c>，最终为 0 发。
    /// 改成 1.7 饱和的 <c>(int)</c> 转换时，敌方倍率大于 1 不受上限约束，越界会得到 <c>int.MaxValue</c> 发。
    /// </remarks>
    internal static int GeneratedCharges(int maxCharges, float ammoFactor, float randomFactor, bool capAtMaxCharges)
    {
        int charges = UnityMathf.RoundToInt(maxCharges * ammoFactor * randomFactor);
        if (capAtMaxCharges)
        {
            charges = Math.Min(maxCharges, charges);
        }

        return Math.Max(0, charges);
    }

    private static class ReflectionCache
    {
        // StaticCacheLifecycle: 进程级；兼容补丁第一次执行时解析，只缓存委托与字段访问器，不持有游戏对象，无需清理。
        internal static readonly AccessTools.FieldRef<bool>? AmmoEnabled;
        internal static readonly AccessTools.FieldRef<float>? EnemyAmmoFactor;
        internal static readonly Action<CompApparelReloadable>? TryAutoReload;

        // 原版 protected 字段，1.7 仍在；按类型化引用读写，代替 FieldInfo.SetValue 的装箱。
        internal static readonly AccessTools.FieldRef<CompApparelVerbOwner_Charged, int>? RemainingCharges;

        static ReflectionCache()
        {
            Type? core = AccessTools.TypeByName("yayoCombat.YayoCombatCore");
            if (core is not null)
            {
                AmmoEnabled = CompatAccessors.StaticField<bool>(AccessTools.Field(core, "ammo"));
                EnemyAmmoFactor = CompatAccessors.StaticField<float>(AccessTools.Field(core, "s_enemyAmmo"));
            }

            Type? reloadUtility = AccessTools.TypeByName("yayoCombat.reloadUtility");
            if (reloadUtility is not null)
            {
                TryAutoReload = CompatAccessors.StaticMethod<Action<CompApparelReloadable>>(
                    AccessTools.Method(reloadUtility, "TryAutoReload", [typeof(CompApparelReloadable)]));
            }

            try
            {
                RemainingCharges = AccessTools.FieldRefAccess<CompApparelVerbOwner_Charged, int>("remainingCharges");
            }
            catch (Exception)
            {
                RemainingCharges = null;
            }

            if (AmmoEnabled is null)
            {
                CompatAccessors.ReportUnbound("Yayo's Combat", "yayoCombat.YayoCombatCore.ammo");
            }

            if (EnemyAmmoFactor is null)
            {
                CompatAccessors.ReportUnbound("Yayo's Combat", "yayoCombat.YayoCombatCore.s_enemyAmmo");
            }

            if (TryAutoReload is null)
            {
                CompatAccessors.ReportUnbound("Yayo's Combat", "yayoCombat.reloadUtility.TryAutoReload");
            }

            if (RemainingCharges is null)
            {
                CompatAccessors.ReportUnbound("Yayo's Combat", "RimWorld.CompApparelVerbOwner_Charged.remainingCharges");
            }
        }
    }
}
