using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace Mugirl
{
    public class CompProperties_MilkingDeviceReleaseEffect : CompProperties
    {
        public class SoundWithParams
        {
            public SoundDef sound;
            public int startTick = 0;
            public int loopInterval = 60;
            public int endTick = 60;
        }

        public class FleckWithParams
        {
            public FleckDef fleck;
            public int startTick = 0;
            public int loopInterval = 30;
            public int endTick = 60;
            public float scale = 0.42f;
            public float speedMin = 0.2f;
            public float speedMax = 0.5f;
        }

        public class TextWithParams
        {
            public string text;
            public int startTick = 0;
            public Color color = Color.white;
        }

        public CompProperties_MilkingDeviceReleaseEffect()
        {
            compClass = typeof(CompMilkingDeviceReleaseEffect);
        }

        public int durationTicks = 60;
        public List<SoundWithParams> sounds;
        public List<FleckWithParams> flecks;
        public List<TextWithParams> texts;
    }

    public class CompMilkingDeviceReleaseEffect : ThingComp
    {
        private int effectTicksRemaining;
        private int age;
        private List<int> nextSoundTicks = new List<int>();
        private List<int> nextFleckTicks = new List<int>();
        private List<int> nextTextTicks = new List<int>();

        public CompProperties_MilkingDeviceReleaseEffect Props => props as CompProperties_MilkingDeviceReleaseEffect;

        private Pawn Wearer => (parent as Apparel)?.Wearer;

        public void Trigger()
        {
            CompProperties_MilkingDeviceReleaseEffect releaseProps = Props;
            Pawn wearer = Wearer;
            if (releaseProps == null || wearer?.Map == null)
            {
                return;
            }

            effectTicksRemaining = Mathf.Max(1, releaseProps.durationTicks);
            age = 0;
            InitializeSchedules(releaseProps);
            RunEffectFrame(wearer, releaseProps);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (effectTicksRemaining <= 0)
            {
                return;
            }

            Pawn wearer = Wearer;
            CompProperties_MilkingDeviceReleaseEffect releaseProps = Props;
            if (releaseProps == null || wearer?.Map == null)
            {
                effectTicksRemaining = 0;
                return;
            }

            age++;
            effectTicksRemaining--;
            RunEffectFrame(wearer, releaseProps);
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref effectTicksRemaining, "effectTicksRemaining", 0);
            Scribe_Values.Look(ref age, "effectAge", 0);
            Scribe_Collections.Look(ref nextSoundTicks, "nextSoundTicks", LookMode.Value);
            Scribe_Collections.Look(ref nextFleckTicks, "nextFleckTicks", LookMode.Value);
            Scribe_Collections.Look(ref nextTextTicks, "nextTextTicks", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (nextSoundTicks == null)
                {
                    nextSoundTicks = new List<int>();
                }

                if (nextFleckTicks == null)
                {
                    nextFleckTicks = new List<int>();
                }

                if (nextTextTicks == null)
                {
                    nextTextTicks = new List<int>();
                }
            }
        }

        private void InitializeSchedules(CompProperties_MilkingDeviceReleaseEffect releaseProps)
        {
            nextSoundTicks = BuildSoundSchedule(releaseProps.sounds);
            nextFleckTicks = BuildFleckSchedule(releaseProps.flecks);
            nextTextTicks = BuildTextSchedule(releaseProps.texts);
        }

        private static List<int> BuildSoundSchedule(List<CompProperties_MilkingDeviceReleaseEffect.SoundWithParams> sounds)
        {
            List<int> ticks = new List<int>();
            if (sounds == null)
            {
                return ticks;
            }

            for (int i = 0; i < sounds.Count; i++)
            {
                CompProperties_MilkingDeviceReleaseEffect.SoundWithParams soundParams = sounds[i];
                ticks.Add(soundParams == null ? -1 : Mathf.Max(0, soundParams.startTick));
            }

            return ticks;
        }

        private static List<int> BuildFleckSchedule(List<CompProperties_MilkingDeviceReleaseEffect.FleckWithParams> flecks)
        {
            List<int> ticks = new List<int>();
            if (flecks == null)
            {
                return ticks;
            }

            for (int i = 0; i < flecks.Count; i++)
            {
                CompProperties_MilkingDeviceReleaseEffect.FleckWithParams fleckParams = flecks[i];
                ticks.Add(fleckParams == null ? -1 : Mathf.Max(0, fleckParams.startTick));
            }

            return ticks;
        }

        private static List<int> BuildTextSchedule(List<CompProperties_MilkingDeviceReleaseEffect.TextWithParams> texts)
        {
            List<int> ticks = new List<int>();
            if (texts == null)
            {
                return ticks;
            }

            for (int i = 0; i < texts.Count; i++)
            {
                CompProperties_MilkingDeviceReleaseEffect.TextWithParams textParams = texts[i];
                ticks.Add(textParams == null ? -1 : Mathf.Max(0, textParams.startTick));
            }

            return ticks;
        }

        private void RunEffectFrame(Pawn wearer, CompProperties_MilkingDeviceReleaseEffect releaseProps)
        {
            Map map = wearer?.Map;
            if (releaseProps == null || map == null)
            {
                return;
            }

            if (nextTextTicks == null)
            {
                nextTextTicks = new List<int>();
            }

            if (nextSoundTicks == null)
            {
                nextSoundTicks = new List<int>();
            }

            if (nextFleckTicks == null)
            {
                nextFleckTicks = new List<int>();
            }

            if (releaseProps.texts != null)
            {
                for (int i = 0; i < releaseProps.texts.Count; i++)
                {
                    if (i < nextTextTicks.Count && nextTextTicks[i] != -1 && age == nextTextTicks[i])
                    {
                        var textParams = releaseProps.texts[i];
                        if (textParams == null)
                        {
                            nextTextTicks[i] = -1;
                            continue;
                        }

                        if (!string.IsNullOrEmpty(textParams.text))
                        {
                            MoteMaker.ThrowText(wearer.DrawPos, map, textParams.text, textParams.color, 4f);
                        }

                        nextTextTicks[i] = -1;
                    }
                }
            }

            if (releaseProps.sounds != null)
            {
                for (int i = 0; i < releaseProps.sounds.Count; i++)
                {
                    var soundParams = releaseProps.sounds[i];
                    if (soundParams == null || i >= nextSoundTicks.Count || nextSoundTicks[i] == -1)
                    {
                        continue;
                    }

                    int startTick = Mathf.Max(0, soundParams.startTick);
                    int endTick = Mathf.Max(startTick, soundParams.endTick);
                    if (age >= startTick && age <= endTick && age >= nextSoundTicks[i])
                    {
                        SoundInfo info = SoundInfo.InMap(new TargetInfo(wearer.Position, map));
                        soundParams.sound?.PlayOneShot(info);
                        nextSoundTicks[i] = age + Mathf.Max(1, soundParams.loopInterval);
                    }
                }
            }

            if (releaseProps.flecks != null)
            {
                for (int i = 0; i < releaseProps.flecks.Count; i++)
                {
                    var fleckParams = releaseProps.flecks[i];
                    if (fleckParams == null || i >= nextFleckTicks.Count || nextFleckTicks[i] == -1)
                    {
                        continue;
                    }

                    int startTick = Mathf.Max(0, fleckParams.startTick);
                    int endTick = Mathf.Max(startTick, fleckParams.endTick);
                    if (age >= startTick && age <= endTick && age >= nextFleckTicks[i])
                    {
                        IntVec3 offset = new IntVec3(Rand.RangeInclusive(-1, 1), 0, Rand.RangeInclusive(0, 1));
                        IntVec3 pos = wearer.Position + offset;
                        if (pos.InBounds(map) && fleckParams.fleck != null)
                        {
                            float minSpeed = Mathf.Min(fleckParams.speedMin, fleckParams.speedMax);
                            float maxSpeed = Mathf.Max(fleckParams.speedMin, fleckParams.speedMax);
                            FleckCreationData data = FleckMaker.GetDataStatic(pos.ToVector3Shifted(), map, fleckParams.fleck, Mathf.Max(0.01f, fleckParams.scale));
                            data.velocityAngle = 0f;
                            data.velocitySpeed = Rand.Range(minSpeed, maxSpeed);
                            map.flecks.CreateFleck(data);
                        }

                        nextFleckTicks[i] = age + Mathf.Max(1, fleckParams.loopInterval);
                    }
                }
            }
        }
    }
}
