using System.Diagnostics.CodeAnalysis;

namespace Mugirl.Features.Mounting;

// 骑乘远程 verb 的施放者登记：verb 属于骑手的武器，开火时临时以载具为施放者（MountedVerbScope）。
public static partial class MountedCombatController
{
    // StaticCacheLifecycle: 每局游戏的“骑乘 verb → 原施放者（通常是骑手）”登记。下骑、卸下装备时逐项移除；
    // 生命周期中枢在原瞬态重置点调用 ResetTransientState 清空。
    private static readonly Dictionary<Verb, Thing?> OriginalCasters = new();

    // 还原登记过的原施放者并清空瞬态索引；不调用 Verb.Reset，保留正在进行的连射状态。
    public static void ResetTransientState()
    {
        foreach (KeyValuePair<Verb, Thing?> entry in OriginalCasters)
        {
            if (entry.Value is not null)
            {
                entry.Key.caster = entry.Value;
            }
        }

        OriginalCasters.Clear();
        MountedWarmupOverride.Clear();
    }

    public static bool TryGetWarmupTimeOverride(Verb? verb, ref float warmupTime)
    {
        if (MountedWarmupOverride.TryGet(verb, out float overrideValue))
        {
            warmupTime = overrideValue;
            return true;
        }

        return false;
    }

    public static bool IsMountedVerb([NotNullWhen(true)] Verb? verb)
    {
        return verb is not null && OriginalCasters.ContainsKey(verb);
    }

    // 近身目标需要暂时取消最小射程。VerbProperties 由同一武器 Def 的所有实例共享，
    // 所以只能在 using 作用域内修改并立即还原，不能跨出 TryStartCastOn / TryFindShootLineFromTo 调用。
    private readonly struct MountedMinRangeOverride : IDisposable
    {
        private readonly VerbProperties? verbProps;
        private readonly float originalMinRange;

        public MountedMinRangeOverride(Verb verb, float? minRange)
        {
            if (minRange is { } value && verb.verbProps is { } props)
            {
                verbProps = props;
                originalMinRange = props.minRange;
                props.minRange = value;
            }
            else
            {
                verbProps = null;
                originalMinRange = 0f;
            }
        }

        public void Dispose()
        {
            if (verbProps is not null)
            {
                verbProps.minRange = originalMinRange;
            }
        }
    }

    private static void EnsureVerbCaster(CompMount comp, Verb verb)
    {
        Pawn? carrier = comp.MooPawn;
        if (carrier is null || OriginalCasters.ContainsKey(verb))
        {
            return;
        }

        // 登记时 verb 已被换成载具（异常中断遗留）则记为骑手，保证下骑后能还原。
        Pawn? rider = comp.MountedPawn;
        OriginalCasters.Add(verb, verb.caster == carrier && rider is not null ? rider : verb.caster);
    }

    private static MountedVerbScope MountedCasterScope(CompMount comp, Verb verb)
    {
        EnsureVerbCaster(comp, verb);
        return new MountedVerbScope(verb, comp.MooPawn);
    }

    private static void RestoreVerbCaster(Pawn? rider)
    {
        List<ThingWithComps>? equipment = rider?.equipment?.AllEquipmentListForReading;
        if (rider is null || equipment is null)
        {
            return;
        }

        for (int i = 0; i < equipment.Count; i++)
        {
            List<Verb>? verbs = equipment[i].GetComp<CompEquippable>()?.AllVerbs;
            if (verbs is null)
            {
                continue;
            }

            for (int j = 0; j < verbs.Count; j++)
            {
                Verb verb = verbs[j];
                if (OriginalCasters.TryGetValue(verb, out Thing? originalCaster))
                {
                    verb.Reset();
                    verb.caster = originalCaster ?? rider;
                    ForgetVerb(verb, resetVerb: false);
                }
                else if (verb.caster != rider)
                {
                    verb.Reset();
                    verb.caster = rider;
                }
            }
        }
    }

    private static void ForgetVerb(Verb? verb, bool resetVerb = true)
    {
        if (verb is null)
        {
            return;
        }

        if (OriginalCasters.TryGetValue(verb, out Thing? originalCaster))
        {
            if (resetVerb)
            {
                verb.Reset();
            }

            if (originalCaster is not null)
            {
                verb.caster = originalCaster;
            }

            OriginalCasters.Remove(verb);
        }

        MountedWarmupOverride.Forget(verb);
    }
}
