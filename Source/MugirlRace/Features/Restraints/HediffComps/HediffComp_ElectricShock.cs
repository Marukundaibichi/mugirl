using System.Reflection;
using HarmonyLib;
using Verse.Sound;

namespace Mugirl.Features.Restraints;

public class HediffComp_ElectricShock : HediffComp
{
    // StaticCacheLifecycle: 进程级字段访问器，首次使用本类型时解析一次；不持有游戏对象，无需清理。
    // Pawn_DrawTracker.jitterer 在 1.7 是私有字段（已核对 rw17-game）；找不到时为 null，抖动静默关闭并警告一次。
    private static readonly AccessTools.FieldRef<Pawn_DrawTracker, JitterHandler>? JittererRef = CreateJittererRef();

    private int mugirlShockJitterTicksLeft;
    private Dictionary<ThingDef, int> mugirlShockFilthTimers = new Dictionary<ThingDef, int>();
    private readonly List<ThingDef> tmpStaleFilthDefs = new List<ThingDef>();

    private HediffCompProperties_ElectricShock? Properties => props as HediffCompProperties_ElectricShock;

    public override void CompPostMake()
    {
        base.CompPostMake();
        EnsureFilthTimers(Properties);
    }

    // 电击效果在 Hediff 真正加入后执行。1.6 放在 CompPostMake：此时 Hediff 尚未加入，
    // 若随后与已有 Hediff 合并或加入被拒绝，声音、眩晕、灼伤仍会发生。
    public override void CompPostPostAdd(DamageInfo? dinfo)
    {
        base.CompPostPostAdd(dinfo);

        HediffCompProperties_ElectricShock? shockProps = Properties;
        Pawn? pawn = Pawn;
        if (shockProps == null || pawn == null)
        {
            return;
        }

        PlayShockSounds(pawn, shockProps);
        RemoveConfiguredHediffs(pawn, shockProps);
        ApplyConfiguredHediffs(pawn, shockProps);
        ApplyStun(pawn, shockProps);
    }

    public override void CompPostTick(ref float severityAdjustment)
    {
        base.CompPostTick(ref severityAdjustment);

        HediffCompProperties_ElectricShock? shockProps = Properties;
        Pawn? pawn = Pawn;
        if (shockProps == null || pawn == null || pawn.Dead)
        {
            return;
        }

        if (mugirlShockJitterTicksLeft > 0)
        {
            mugirlShockJitterTicksLeft--;
            TryAddJitter(pawn, shockProps);
        }

        TickFilth(pawn, shockProps);
    }

    public override void CompExposeData()
    {
        base.CompExposeData();

        Scribe_Values.Look(ref mugirlShockJitterTicksLeft, "mugirlShockJitterTicksLeft", 0);
        Scribe_Collections.Look(ref mugirlShockFilthTimers, "mugirlShockFilthTimers", LookMode.Def, LookMode.Value);
        if (Scribe.mode == LoadSaveMode.PostLoadInit)
        {
            EnsureFilthTimers(Properties);
        }
    }

    private static AccessTools.FieldRef<Pawn_DrawTracker, JitterHandler>? CreateJittererRef()
    {
        FieldInfo? field = AccessTools.DeclaredField(typeof(Pawn_DrawTracker), "jitterer");
        if (field == null || field.FieldType != typeof(JitterHandler))
        {
            MugirlLog.WarningOnce("EleShock.JittererMissing", "Pawn_DrawTracker.jitterer was not found; electric-shock jitter is disabled.");
            return null;
        }

        return AccessTools.FieldRefAccess<Pawn_DrawTracker, JitterHandler>(field);
    }

    private static void PlayShockSounds(Pawn pawn, HediffCompProperties_ElectricShock shockProps)
    {
        if (pawn.Map == null || shockProps.shockSounds == null)
        {
            return;
        }

        TargetInfo target = new TargetInfo(pawn.Position, pawn.Map, false);
        for (int i = 0; i < shockProps.shockSounds.Count; i++)
        {
            shockProps.shockSounds[i]?.PlayOneShot(target);
        }
    }

    // 计时器只需在创建与读档时与配置同步；配置在运行期不变，1.6 每 tick 调用一次属于多余开销。
    private void EnsureFilthTimers(HediffCompProperties_ElectricShock? shockProps)
    {
        mugirlShockFilthTimers ??= new Dictionary<ThingDef, int>();

        if (shockProps?.filthSpawnEntries == null)
        {
            mugirlShockFilthTimers.Clear();
            return;
        }

        for (int i = 0; i < shockProps.filthSpawnEntries.Count; i++)
        {
            HediffCompProperties_ElectricShock.FilthEntry? entry = shockProps.filthSpawnEntries[i];
            if (entry?.filthDef == null || entry.filthAmount <= 0f)
            {
                continue;
            }

            if (!mugirlShockFilthTimers.ContainsKey(entry.filthDef))
            {
                mugirlShockFilthTimers[entry.filthDef] = Math.Max(1, entry.spawnInterval);
            }
        }

        tmpStaleFilthDefs.Clear();
        foreach (ThingDef filthDef in mugirlShockFilthTimers.Keys)
        {
            if (!IsConfiguredFilthDef(shockProps.filthSpawnEntries, filthDef))
            {
                tmpStaleFilthDefs.Add(filthDef);
            }
        }

        for (int i = 0; i < tmpStaleFilthDefs.Count; i++)
        {
            mugirlShockFilthTimers.Remove(tmpStaleFilthDefs[i]);
        }

        tmpStaleFilthDefs.Clear();
    }

    private static bool IsConfiguredFilthDef(List<HediffCompProperties_ElectricShock.FilthEntry> entries, ThingDef? filthDef)
    {
        if (filthDef == null)
        {
            return false;
        }

        for (int i = 0; i < entries.Count; i++)
        {
            HediffCompProperties_ElectricShock.FilthEntry? entry = entries[i];
            if (entry?.filthDef == filthDef && entry.filthAmount > 0f)
            {
                return true;
            }
        }

        return false;
    }

    private void ApplyStun(Pawn pawn, HediffCompProperties_ElectricShock shockProps)
    {
        // UnityMathf.RoundToInt 保留 1.6 的 Mono 取整（银行家舍入，NaN/越界得 int.MinValue，随后被 Max 截为 0）。
        int stunTicks = Math.Max(0, UnityMathf.RoundToInt(shockProps.stunDuration * 60f));
        if (stunTicks <= 0)
        {
            return;
        }

        pawn.stances?.stunner?.StunFor(stunTicks, pawn, true, true, true);
        mugirlShockJitterTicksLeft = stunTicks;
    }

    private static void ApplyConfiguredHediffs(Pawn pawn, HediffCompProperties_ElectricShock shockProps)
    {
        if (pawn.health == null || shockProps.hediffsToApplyWithParams == null)
        {
            return;
        }

        bool hostile = PlayerFactionUtility.IsHostileToPlayer(pawn);
        for (int i = 0; i < shockProps.hediffsToApplyWithParams.Count; i++)
        {
            TryAddHediffWithParams(pawn, shockProps, shockProps.hediffsToApplyWithParams[i], hostile);
        }
    }

    private void RemoveConfiguredHediffs(Pawn pawn, HediffCompProperties_ElectricShock shockProps)
    {
        if (pawn.health?.hediffSet == null || shockProps.hediffsToRemove == null)
        {
            return;
        }

        for (int i = 0; i < shockProps.hediffsToRemove.Count; i++)
        {
            HediffDef? hediffDef = shockProps.hediffsToRemove[i];
            if (hediffDef == null)
            {
                continue;
            }

            Hediff? hediff;
            while ((hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef)) != null)
            {
                // 配置要移除自身 Def 时不能移除正在加入的这个 Hediff。
                if (hediff == parent)
                {
                    break;
                }

                pawn.health.RemoveHediff(hediff);
            }
        }
    }

    private static void TryAddHediffWithParams(Pawn pawn, HediffCompProperties_ElectricShock shockProps, HediffCompProperties_ElectricShock.HediffWithParams? item, bool hostile)
    {
        if (item?.hediff == null)
        {
            return;
        }

        BodyPartRecord? part = null;
        List<BodyPartRecord>? allParts = item.bodyPartTarget == null ? null : pawn.RaceProps?.body?.AllParts;
        if (allParts != null)
        {
            for (int i = 0; i < allParts.Count; i++)
            {
                if (allParts[i]?.def == item.bodyPartTarget)
                {
                    part = allParts[i];
                    break;
                }
            }
        }

        Hediff? hediff = pawn.health.AddHediff(item.hediff, part);
        if (hediff == null)
        {
            return;
        }

        hediff.Severity = item.severity >= 0f
            ? item.severity
            : hostile ? UnityMathf.Max(0.001f, shockProps.hostileStunDuration / UnityMathf.Max(0.01f, shockProps.stunDuration)) : 0.1f;
    }

    private static void TryAddJitter(Pawn pawn, HediffCompProperties_ElectricShock shockProps)
    {
        if (pawn.Drawer == null || shockProps.jitterMagnitude <= 0f || JittererRef == null)
        {
            return;
        }

        JitterHandler? jitterer = JittererRef(pawn.Drawer);
        if (jitterer == null)
        {
            return;
        }

        // AddOffset 的方向参数是角度（Quaternion.AngleAxis，原版 Notify_DebugAffected 传 Rand.Range(0, 360)）。
        // 1.6 传 0–2π 的弧度，偏移几乎总朝北并被钳在 JitterMax，表现为位移而非抖动。
        jitterer.AddOffset(shockProps.jitterMagnitude, Rand.Range(0f, 360f));
    }

    private void TickFilth(Pawn pawn, HediffCompProperties_ElectricShock shockProps)
    {
        if (pawn.Map == null || shockProps.filthSpawnEntries == null || mugirlShockFilthTimers == null)
        {
            return;
        }

        for (int i = 0; i < shockProps.filthSpawnEntries.Count; i++)
        {
            HediffCompProperties_ElectricShock.FilthEntry? entry = shockProps.filthSpawnEntries[i];
            if (entry?.filthDef == null || entry.filthAmount <= 0f || !mugirlShockFilthTimers.TryGetValue(entry.filthDef, out int ticksUntilFilth))
            {
                continue;
            }

            ticksUntilFilth--;
            if (ticksUntilFilth > 0)
            {
                mugirlShockFilthTimers[entry.filthDef] = ticksUntilFilth;
                continue;
            }

            mugirlShockFilthTimers[entry.filthDef] = Math.Max(1, entry.spawnInterval);
            int filthCount = GenMath.RoundRandom(entry.filthAmount);
            for (int filthIndex = 0; filthIndex < filthCount; filthIndex++)
            {
                TryMakeSingleFilth(pawn, entry.filthDef, entry.spawnInFacingDirection);
            }
        }
    }

    private static void TryMakeSingleFilth(Pawn pawn, ThingDef filthDef, bool inFacingDirection)
    {
        Map? map = pawn.Map;
        if (map == null)
        {
            return;
        }

        IntVec3 pos = inFacingDirection && pawn.Rotation != Rot4.Invalid
            ? pawn.Position + pawn.Rotation.FacingCell
            : pawn.Position;

        if (pos.InBounds(map))
        {
            Thing filth = ThingMaker.MakeThing(filthDef);
            GenPlace.TryPlaceThing(filth, pos, map, ThingPlaceMode.Near);
        }
    }
}
