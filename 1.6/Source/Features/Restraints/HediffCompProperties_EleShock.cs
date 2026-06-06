using HarmonyLib;
using System.Collections.Generic;
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
        private int currentTickCount = 0; // 当前计时器
        private int remainingCycles = 0; // 剩余周期数
        private int jitterTicksLeft = 0; // 剩余震动时间
        private Dictionary<ThingDef, int> filthTimers = new Dictionary<ThingDef, int>(); // 污物计时器

        private CompProperties_EleShock Properties => (CompProperties_EleShock)this.props;

        public override void CompPostMake()
        {
            base.CompPostMake();
            remainingCycles = Properties.maxCycles;
            currentTickCount = Properties.effectCycleInterval;

            foreach (var sound in Properties.shockSounds)
            {
                sound?.PlayOneShot(new TargetInfo(Pawn.Position, Pawn.Map, false));
            }

            ApplyStunO();

            if (!Properties.filthSpawnEntries.NullOrEmpty())
            {
                foreach (var entry in Properties.filthSpawnEntries)
                {
                    if (entry.filthDef != null)
                    {
                        filthTimers[entry.filthDef] = entry.spawnInterval;
                    }
                }
            }
        }

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);

            currentTickCount--;
            if (currentTickCount <= 0 && remainingCycles > 0)
            {
                remainingCycles--; // 剩余周期
                currentTickCount = Properties.effectCycleInterval; // 重置间隔
            }

            if (jitterTicksLeft > 0)
            {
                jitterTicksLeft--;
                float angle = Rand.Range(0f, 2f * Mathf.PI);
                Traverse.Create(Pawn.Drawer).Field<JitterHandler>("jitterer").Value
                    .AddOffset(Properties.jitterMagnitude, angle);
            }

            if (Pawn.Map != null && !Properties.filthSpawnEntries.NullOrEmpty())
            {
                foreach (var entry in Properties.filthSpawnEntries)
                {
                    if (entry.filthDef == null || entry.filthAmount <= 0f) continue;
                    if (!filthTimers.ContainsKey(entry.filthDef)) continue;

                    filthTimers[entry.filthDef]--;
                    if (filthTimers[entry.filthDef] <= 0)
                    {
                        filthTimers[entry.filthDef] = entry.spawnInterval;
                        TryMakeSingleFilth(Pawn, entry.filthDef, entry.spawnInFacingDirection);
                    }
                }
            }
        }

        private void ApplyStunO()
        {
            int stunTicks = Mathf.RoundToInt(Properties.stunDuration * 60f);
            Pawn.stances?.stunner?.StunFor(stunTicks, Pawn, true, true, true);
            jitterTicksLeft = stunTicks;
        }

        private void TryAddHediffWithParams(CompProperties_EleShock.HediffWithParams item, bool hostile)
        {
            BodyPartRecord part = null;
            if (!string.IsNullOrEmpty(item.bodyPartTarget))
            {
                List<BodyPartRecord> allParts = Pawn.RaceProps.body.AllParts;
                for (int i = 0; i < allParts.Count; i++)
                {
                    if (allParts[i].def.defName == item.bodyPartTarget)
                    {
                        part = allParts[i];
                        break;
                    }
                }
            }

            var h = Pawn.health.AddHediff(item.hediff, part);
            h.Severity = item.severity >= 0f
                ? item.severity
                : hostile ? Properties.hostileStunDuration / Properties.stunDuration : 0.1f;
        }

        private void TryMakeSingleFilth(Pawn pawn, ThingDef filthDef, bool inFacingDirection)
        {
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
        }

    }
}
