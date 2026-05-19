using RimWorld;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MooGirl
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

        public CompProperties_MilkingDeviceReleaseEffect Props => (CompProperties_MilkingDeviceReleaseEffect)props;

        private Pawn Wearer => (parent as Apparel)?.Wearer;

        public void Trigger()
        {
            Pawn wearer = Wearer;
            if (wearer?.Map == null)
            {
                return;
            }

            effectTicksRemaining = Mathf.Max(1, Props.durationTicks);
            age = 0;
            InitializeSchedules();
            RunEffectFrame(wearer);
        }

        public override void CompTick()
        {
            base.CompTick();
            if (effectTicksRemaining <= 0)
            {
                return;
            }

            Pawn wearer = Wearer;
            if (wearer?.Map == null)
            {
                effectTicksRemaining = 0;
                return;
            }

            age++;
            effectTicksRemaining--;
            RunEffectFrame(wearer);
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
                if (nextSoundTicks == null) nextSoundTicks = new List<int>();
                if (nextFleckTicks == null) nextFleckTicks = new List<int>();
                if (nextTextTicks == null) nextTextTicks = new List<int>();
            }
        }

        private void InitializeSchedules()
        {
            nextSoundTicks = Props.sounds?.Select(s => s.startTick).ToList() ?? new List<int>();
            nextFleckTicks = Props.flecks?.Select(f => f.startTick).ToList() ?? new List<int>();
            nextTextTicks = Props.texts?.Select(t => t.startTick).ToList() ?? new List<int>();
        }

        private void RunEffectFrame(Pawn wearer)
        {
            if (Props.texts != null)
            {
                for (int i = 0; i < Props.texts.Count; i++)
                {
                    if (i < nextTextTicks.Count && nextTextTicks[i] != -1 && age == nextTextTicks[i])
                    {
                        var textParams = Props.texts[i];
                        if (!string.IsNullOrEmpty(textParams.text))
                        {
                            MoteMaker.ThrowText(wearer.DrawPos, wearer.Map, textParams.text, textParams.color, 4f);
                        }

                        nextTextTicks[i] = -1;
                    }
                }
            }

            if (Props.sounds != null)
            {
                for (int i = 0; i < Props.sounds.Count; i++)
                {
                    var soundParams = Props.sounds[i];
                    if (i < nextSoundTicks.Count && age >= soundParams.startTick && age <= soundParams.endTick && age >= nextSoundTicks[i])
                    {
                        SoundInfo info = SoundInfo.InMap(new TargetInfo(wearer.Position, wearer.Map));
                        soundParams.sound?.PlayOneShot(info);
                        nextSoundTicks[i] = age + Mathf.Max(1, soundParams.loopInterval);
                    }
                }
            }

            if (Props.flecks != null)
            {
                for (int i = 0; i < Props.flecks.Count; i++)
                {
                    var fleckParams = Props.flecks[i];
                    if (i < nextFleckTicks.Count && age >= fleckParams.startTick && age <= fleckParams.endTick && age >= nextFleckTicks[i])
                    {
                        IntVec3 offset = new IntVec3(Rand.RangeInclusive(-1, 1), 0, Rand.RangeInclusive(0, 1));
                        IntVec3 pos = wearer.Position + offset;
                        if (pos.InBounds(wearer.Map) && fleckParams.fleck != null)
                        {
                            FleckCreationData data = FleckMaker.GetDataStatic(pos.ToVector3Shifted(), wearer.Map, fleckParams.fleck, fleckParams.scale);
                            data.velocityAngle = 0f;
                            data.velocitySpeed = Rand.Range(fleckParams.speedMin, fleckParams.speedMax);
                            wearer.Map.flecks.CreateFleck(data);
                        }

                        nextFleckTicks[i] = age + Mathf.Max(1, fleckParams.loopInterval);
                    }
                }
            }
        }
    }
}
