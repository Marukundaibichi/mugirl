using System.Reflection;
using HarmonyLib;

namespace Mugirl.Compatibility.RunAndGun;

/// <summary>
/// RunAndGun：判断 Pawn 是否在移动中施放（此时轮盘不接管火力链）。
/// </summary>
/// <remarks>
/// 两个版本都核对过：旧版 roolo.RunAndGun.kotobike（1.6）的 <c>RunAndGun.CompRunAndGun.isEnabled</c> 是字段；
/// 续作 MemeGoddess.RunAndGun 的 1.7 构建改为属性（底层字段 <c>_isEnabled</c>）。两者的姿态类都是
/// <c>RunAndGun.Stance_RunAndGun</c> 与 <c>RunAndGun.Stance_RunAndGun_Cooldown</c>。
/// </remarks>
internal static class RunAndGunCompatibility
{
    internal static bool Active => ModCompat.RunAndGun;

    internal static void RegisterHooks()
    {
        if (Active)
        {
            Mugirl.Features.WeaponWheel.WeaponWheelHooks.Current.AddEligibilityVeto("RunAndGun", IsMovingCast);
        }
    }

    internal static bool IsMovingCast(Pawn? pawn)
    {
        if (pawn is null || pawn.CurJobDef != JobDefOf.Goto
            || ReflectionCache.CompType is not { } compType || !ReflectionCache.HasEnabledMember)
        {
            return false;
        }

        // 与 1.6 相同按短类名判断（GetType().Name 由运行时缓存，不分配）。
        if (pawn.stances?.curStance?.GetType().Name is "Stance_RunAndGun" or "Stance_RunAndGun_Cooldown")
        {
            return true;
        }

        List<ThingComp>? comps = pawn.AllComps;
        if (comps is null)
        {
            return false;
        }

        for (int i = 0; i < comps.Count; i++)
        {
            ThingComp comp = comps[i];
            if (comp is null || !compType.IsInstanceOfType(comp))
            {
                continue;
            }

            // 成员存在但不是 bool 时没有访问器，与 1.6 的 “GetValue 结果不是 bool 即 false” 相同。
            return ReflectionCache.IsEnabled?.Invoke(comp) == true;
        }

        return false;
    }

    private static class ReflectionCache
    {
        // StaticCacheLifecycle: 进程级；资格补丁第一次执行时解析，只缓存类型与委托，不持有游戏对象，无需清理。
        internal static readonly Type? CompType;
        internal static readonly bool HasEnabledMember;
        internal static readonly Func<ThingComp, bool>? IsEnabled;

        static ReflectionCache()
        {
            CompType = AccessTools.TypeByName("RunAndGun.CompRunAndGun");
            if (CompType is null)
            {
                CompatAccessors.ReportUnbound("RunAndGun", "RunAndGun.CompRunAndGun");
                return;
            }

            // 1.6 先读属性、没有属性才读字段，这里保持同一优先级。
            PropertyInfo? property = AccessTools.Property(CompType, "isEnabled");
            FieldInfo? field = property is null ? AccessTools.Field(CompType, "isEnabled") : null;
            HasEnabledMember = property is not null || field is not null;
            IsEnabled = property is not null
                ? CompatAccessors.InstanceGetter<ThingComp, bool>(property)
                : CompatAccessors.InstanceGetter<ThingComp, bool>(field);
            if (IsEnabled is null)
            {
                CompatAccessors.ReportUnbound("RunAndGun", "RunAndGun.CompRunAndGun.isEnabled");
            }
        }
    }
}
