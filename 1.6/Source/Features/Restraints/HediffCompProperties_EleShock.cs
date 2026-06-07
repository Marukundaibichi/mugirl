using HarmonyLib;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MooGirl
{
    public class CompProperties_EleShock : HediffCompProperties
    {
        public List<SoundDef> shockSounds;
        public List<HediffDef> hediffsToRemove;
        public List<HediffWithParams> hediffsToApplyWithParams;
        public List<FilthEntry> filthSpawnEntries;
        public float stunDuration = 2f; // 电击晕厥时间
        public float hostileStunDuration = 4f; // 敌对单位的电击晕厥时间
        public float jitterMagnitude = 0.3f; // 震动幅度
        public int effectCycleInterval = 200; // 电击效果周期间隔
        public int maxCycles = 1; // 最多几个周期

        public CompProperties_EleShock()
        {
            compClass = typeof(HediffComp_EleShock);
        }

        public class HediffWithParams
        {
            public HediffDef hediff; // 要应用的 Hediff
            public float severity = -1f; // Hediff 的严重程度
            public string bodyPartTarget; // 目标身体部位
        }

        public class FilthEntry
        {
            public ThingDef filthDef; // 污物类型
            public float filthAmount = 3f; // 污物数量
            public int spawnInterval = 60; // 生成间隔
            public bool spawnInFacingDirection = false;  // 是否朝向方向生成
        }
    }

    public class HediffComp_EleShock : HediffComp
    {
        // StaticCacheLifecycle: process-level reflection cache for Pawn_DrawTracker.jitterer; no game objects are retained.
        private static readonly FieldInfo JittererField = AccessTools.Field(typeof(Pawn_DrawTracker), "jitterer");

        private int currentTickCount = 0; // 当前计时器
        private int remainingCycles = 0; // 剩余周期数
        private int jitterTicksLeft = 0; // 剩余震动时间
        private Dictionary<ThingDef, int> filthTimers = new Dictionary<ThingDef, int>(); // 污物计时器
        private readonly List<ThingDef> tmpStaleFilthDefs = new List<ThingDef>();

        private CompProperties_EleShock Properties => props as CompProperties_EleShock;

        public override void CompPostMake()
        {
            base.CompPostMake();

            CompProperties_EleShock shockProps = Properties;
            Pawn pawn = Pawn;
            if (shockProps == null || pawn == null)
            {
                return;
            }

            remainingCycles = Mathf.Max(0, shockProps.maxCycles);
            currentTickCount = Mathf.Max(1, shockProps.effectCycleInterval);

            PlayShockSounds(pawn, shockProps);
            RemoveConfiguredHediffs(pawn, shockProps);
            ApplyConfiguredHediffs(pawn, shockProps);
            ApplyStun(pawn, shockProps);
            EnsureFilthTimers(shockProps);
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            CompProperties_EleShock shockProps = Properties;
            Pawn pawn = Pawn;
            if (shockProps == null || pawn == null || pawn.Dead)
            {
                return;
            }

            EnsureFilthTimers(shockProps);

            if (remainingCycles > 0)
            {
                currentTickCount--;
                if (currentTickCount <= 0)
                {
                    remainingCycles--; // 剩余周期
                    currentTickCount = Mathf.Max(1, shockProps.effectCycleInterval); // 重置间隔
                }
            }

            if (jitterTicksLeft > 0)
            {
                jitterTicksLeft--;
                TryAddJitter(pawn, shockProps);
            }

            TickFilth(pawn, shockProps);
        }

        private void PlayShockSounds(Pawn pawn, CompProperties_EleShock shockProps)
        {
            if (pawn?.Map == null || shockProps?.shockSounds == null)
            {
                return;
            }

            TargetInfo target = new TargetInfo(pawn.Position, pawn.Map, false);
            for (int i = 0; i < shockProps.shockSounds.Count; i++)
            {
                shockProps.shockSounds[i]?.PlayOneShot(target);
            }
        }

        private void EnsureFilthTimers(CompProperties_EleShock shockProps)
        {
            if (filthTimers == null)
            {
                filthTimers = new Dictionary<ThingDef, int>();
            }

            if (shockProps?.filthSpawnEntries == null)
            {
                filthTimers.Clear();
                return;
            }

            for (int i = 0; i < shockProps.filthSpawnEntries.Count; i++)
            {
                CompProperties_EleShock.FilthEntry entry = shockProps.filthSpawnEntries[i];
                if (entry?.filthDef == null || entry.filthAmount <= 0f)
                {
                    continue;
                }

                if (!filthTimers.ContainsKey(entry.filthDef))
                {
                    filthTimers[entry.filthDef] = Mathf.Max(1, entry.spawnInterval);
                }
            }

            tmpStaleFilthDefs.Clear();
            foreach (ThingDef filthDef in filthTimers.Keys)
            {
                if (!IsConfiguredFilthDef(shockProps.filthSpawnEntries, filthDef))
                {
                    tmpStaleFilthDefs.Add(filthDef);
                }
            }

            for (int i = 0; i < tmpStaleFilthDefs.Count; i++)
            {
                filthTimers.Remove(tmpStaleFilthDefs[i]);
            }
            tmpStaleFilthDefs.Clear();
        }

        private static bool IsConfiguredFilthDef(List<CompProperties_EleShock.FilthEntry> entries, ThingDef filthDef)
        {
            if (entries == null || filthDef == null)
            {
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                CompProperties_EleShock.FilthEntry entry = entries[i];
                if (entry?.filthDef == filthDef && entry.filthAmount > 0f)
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplyStun(Pawn pawn, CompProperties_EleShock shockProps)
        {
            if (pawn == null || shockProps == null)
            {
                return;
            }

            int stunTicks = Mathf.Max(0, Mathf.RoundToInt(shockProps.stunDuration * 60f));
            if (stunTicks <= 0)
            {
                return;
            }

            pawn.stances?.stunner?.StunFor(stunTicks, pawn, true, true, true);
            jitterTicksLeft = stunTicks;
        }

        private void ApplyConfiguredHediffs(Pawn pawn, CompProperties_EleShock shockProps)
        {
            if (pawn?.health == null || shockProps?.hediffsToApplyWithParams == null)
            {
                return;
            }

            bool hostile = MooGirlWildSlaveUtility.IsHostileToPlayer(pawn);
            for (int i = 0; i < shockProps.hediffsToApplyWithParams.Count; i++)
            {
                TryAddHediffWithParams(pawn, shockProps, shockProps.hediffsToApplyWithParams[i], hostile);
            }
        }

        private void RemoveConfiguredHediffs(Pawn pawn, CompProperties_EleShock shockProps)
        {
            if (pawn?.health?.hediffSet == null || shockProps?.hediffsToRemove == null)
            {
                return;
            }

            for (int i = 0; i < shockProps.hediffsToRemove.Count; i++)
            {
                HediffDef hediffDef = shockProps.hediffsToRemove[i];
                if (hediffDef == null)
                {
                    continue;
                }

                Hediff hediff;
                while ((hediff = pawn.health.hediffSet.GetFirstHediffOfDef(hediffDef)) != null)
                {
                    if (hediff == parent)
                    {
                        break;
                    }

                    pawn.health.RemoveHediff(hediff);
                }
            }
        }

        private void TryAddHediffWithParams(Pawn pawn, CompProperties_EleShock shockProps, CompProperties_EleShock.HediffWithParams item, bool hostile)
        {
            if (pawn?.health == null || item?.hediff == null || shockProps == null)
            {
                return;
            }

            BodyPartRecord part = null;
            if (!string.IsNullOrEmpty(item.bodyPartTarget) && pawn.RaceProps?.body?.AllParts != null)
            {
                List<BodyPartRecord> allParts = pawn.RaceProps.body.AllParts;
                for (int i = 0; i < allParts.Count; i++)
                {
                    if (allParts[i]?.def?.defName == item.bodyPartTarget)
                    {
                        part = allParts[i];
                        break;
                    }
                }
            }

            Hediff h = pawn.health.AddHediff(item.hediff, part);
            if (h == null)
            {
                return;
            }

            h.Severity = item.severity >= 0f
                ? item.severity
                : hostile ? Mathf.Max(0.001f, shockProps.hostileStunDuration / Mathf.Max(0.01f, shockProps.stunDuration)) : 0.1f;
        }

        private void TryAddJitter(Pawn pawn, CompProperties_EleShock shockProps)
        {
            if (pawn?.Drawer == null || shockProps == null || shockProps.jitterMagnitude <= 0f)
            {
                return;
            }

            JitterHandler jitterer = JittererField?.GetValue(pawn.Drawer) as JitterHandler;
            if (jitterer == null)
            {
                return;
            }

            float angle = Rand.Range(0f, 2f * Mathf.PI);
            jitterer.AddOffset(shockProps.jitterMagnitude, angle);
        }

        private void TickFilth(Pawn pawn, CompProperties_EleShock shockProps)
        {
            if (pawn?.Map == null || shockProps?.filthSpawnEntries == null || filthTimers == null)
            {
                return;
            }

            for (int i = 0; i < shockProps.filthSpawnEntries.Count; i++)
            {
                CompProperties_EleShock.FilthEntry entry = shockProps.filthSpawnEntries[i];
                if (entry?.filthDef == null || entry.filthAmount <= 0f || !filthTimers.TryGetValue(entry.filthDef, out int ticksUntilFilth))
                {
                    continue;
                }

                ticksUntilFilth--;
                if (ticksUntilFilth > 0)
                {
                    filthTimers[entry.filthDef] = ticksUntilFilth;
                    continue;
                }

                filthTimers[entry.filthDef] = Mathf.Max(1, entry.spawnInterval);
                int filthCount = GenMath.RoundRandom(entry.filthAmount);
                for (int filthIndex = 0; filthIndex < filthCount; filthIndex++)
                {
                    TryMakeSingleFilth(pawn, entry.filthDef, entry.spawnInFacingDirection);
                }
            }
        }

        private void TryMakeSingleFilth(Pawn pawn, ThingDef filthDef, bool inFacingDirection)
        {
            if (pawn?.Map == null || filthDef == null)
            {
                return;
            }

            IntVec3 pos = inFacingDirection && pawn.Rotation != Rot4.Invalid
                ? pawn.Position + pawn.Rotation.FacingCell
                : pawn.Position;

            if (pos.InBounds(pawn.Map))
            {
                var filth = ThingMaker.MakeThing(filthDef);
                GenPlace.TryPlaceThing(filth, pos, pawn.Map, ThingPlaceMode.Near);
            }
        }
        public override void CompExposeData()
        {
            base.CompExposeData();

            Scribe_Values.Look(ref currentTickCount, "currentTickCount", 0);
            Scribe_Values.Look(ref remainingCycles, "remainingCycles", 0);
            Scribe_Values.Look(ref jitterTicksLeft, "jitterTicksLeft", 0);
            Scribe_Collections.Look(ref filthTimers, "filthTimers", LookMode.Def, LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EnsureFilthTimers(Properties);
            }
        }

    }
}
