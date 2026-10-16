using System.Reflection;
using HarmonyLib;

namespace Mugirl.Features.Mounting;

// 骑手近战复用原版 Verb_MeleeAttack 的私有命中、闪避与音效方法，以及 Verb.lastShotTick（冷却起点）。
// 首次使用时一次性解析为强类型委托（1.7 签名已用反编译核对），之后不再有 MethodInfo.Invoke 与 object[] 分配。
// 某个成员解析失败只警告一次，并退回约定的保底值：必中、不可闪避、不播放音效、不写冷却。
internal static class VanillaMeleeAccess
{
    // StaticCacheLifecycle: 进程级委托，只指向原版方法与字段，不持有游戏对象，无需按局清理。
    private static readonly Func<Verb_MeleeAttack, LocalTargetInfo, float>? NonMissChance =
        BindMethod<Func<Verb_MeleeAttack, LocalTargetInfo, float>>("GetNonMissChance", typeof(LocalTargetInfo));

    private static readonly Func<Verb_MeleeAttack, LocalTargetInfo, float>? DodgeChance =
        BindMethod<Func<Verb_MeleeAttack, LocalTargetInfo, float>>("GetDodgeChance", typeof(LocalTargetInfo));

    private static readonly Func<Verb_MeleeAttack, SoundDef>? HitPawnSound =
        BindMethod<Func<Verb_MeleeAttack, SoundDef>>("SoundHitPawn");

    private static readonly Func<Verb_MeleeAttack, SoundDef>? HitBuildingSound =
        BindMethod<Func<Verb_MeleeAttack, SoundDef>>("SoundHitBuilding");

    private static readonly Func<Verb_MeleeAttack, SoundDef>? MissSound =
        BindMethod<Func<Verb_MeleeAttack, SoundDef>>("SoundMiss");

    private static readonly Func<Verb_MeleeAttack, Thing, SoundDef>? DodgeSound =
        BindMethod<Func<Verb_MeleeAttack, Thing, SoundDef>>("SoundDodge", typeof(Thing));

    private static readonly AccessTools.FieldRef<Verb, int>? LastShotTick = BindLastShotTick();

    internal static float GetNonMissChance(Verb_MeleeAttack verb, Thing target)
    {
        return NonMissChance?.Invoke(verb, target) ?? 1f;
    }

    internal static float GetDodgeChance(Verb_MeleeAttack verb, Thing target)
    {
        return DodgeChance?.Invoke(verb, target) ?? 0f;
    }

    internal static SoundDef? SoundHitPawn(Verb_MeleeAttack verb) => HitPawnSound?.Invoke(verb);

    internal static SoundDef? SoundHitBuilding(Verb_MeleeAttack verb) => HitBuildingSound?.Invoke(verb);

    internal static SoundDef? SoundMiss(Verb_MeleeAttack verb) => MissSound?.Invoke(verb);

    internal static SoundDef? SoundDodge(Verb_MeleeAttack verb, Thing target) => DodgeSound?.Invoke(verb, target);

    internal static void SetLastShotTick(Verb verb, int tick)
    {
        if (LastShotTick is not null)
        {
            LastShotTick(verb) = tick;
        }
    }

    private static TDelegate? BindMethod<TDelegate>(string name, params Type[] parameters)
        where TDelegate : Delegate
    {
        MethodInfo? method = AccessTools.DeclaredMethod(typeof(Verb_MeleeAttack), name, parameters);
        if (method is not null)
        {
            try
            {
                return AccessTools.MethodDelegate<TDelegate>(method);
            }
            catch (Exception ex) when (ex is ArgumentException or MemberAccessException)
            {
                MugirlLog.ExceptionOnce("Mount.VanillaMeleeAccess." + name, "Mounted melee could not bind Verb_MeleeAttack." + name + "; using the 1.6 fallback.", ex);
                return null;
            }
        }

        WarnMissing("Verb_MeleeAttack." + name);
        return null;
    }

    private static AccessTools.FieldRef<Verb, int>? BindLastShotTick()
    {
        FieldInfo? field = AccessTools.DeclaredField(typeof(Verb), "lastShotTick");
        if (field?.FieldType == typeof(int))
        {
            return AccessTools.FieldRefAccess<Verb, int>(field);
        }

        WarnMissing("Verb.lastShotTick");
        return null;
    }

    private static void WarnMissing(string member)
    {
        MugirlLog.WarningOnce("Mount.VanillaMeleeAccess." + member, "Mounted melee could not find " + member + " in this RimWorld version; using the 1.6 fallback.");
    }
}
