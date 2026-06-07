using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MooGirl
{
    public class BrainwashPerformancePlayer : IExposable
    {
        private int age = 0;
        private int stunTickTarget = -1;
        private bool stunned = false;
        private List<int> nextTextShowTicks = new List<int>();
        private List<int> nextSoundTicks = new List<int>();
        private List<int> nextFleckTicks = new List<int>();
        private int stunEndTick = -1;
        private bool stunMessageSent = false;
        private Mote attachedMote;
        private bool active = false;

        public bool Active => active;

        public void Start(CompProperties_PerformanceEffect props)
        {
            Stop();

            if (props == null)
                return;

            active = true;
            age = 0;
            stunTickTarget = Mathf.Max(0, props.triggerTicks) + Mathf.Max(0, props.stunDelayTicks);
            stunned = false;
            stunEndTick = -1;
            stunMessageSent = false;

            nextTextShowTicks = new List<int>();
            if (props.texts != null)
            {
                for (int i = 0; i < props.texts.Count; i++)
                {
                    CompProperties_PerformanceEffect.TextWithParams textParams = props.texts[i];
                    nextTextShowTicks.Add(textParams == null ? -1 : Mathf.Max(0, textParams.startTick));
                }
            }

            nextSoundTicks = new List<int>();
            if (props.sounds != null)
            {
                for (int i = 0; i < props.sounds.Count; i++)
                {
                    CompProperties_PerformanceEffect.SoundWithParams soundParams = props.sounds[i];
                    nextSoundTicks.Add(soundParams == null ? -1 : Mathf.Max(0, soundParams.startTick));
                }
            }

            nextFleckTicks = new List<int>();
            if (props.flecks != null)
            {
                for (int i = 0; i < props.flecks.Count; i++)
                {
                    CompProperties_PerformanceEffect.FleckWithParams fleckParams = props.flecks[i];
                    nextFleckTicks.Add(fleckParams == null ? -1 : Mathf.Max(0, fleckParams.startTick));
                }
            }
        }

        public bool Tick(Pawn pawn, CompProperties_PerformanceEffect props, bool sendMessages = true)
        {
            if (!active)
                return false;

            if (pawn == null || pawn.Dead || !pawn.Spawned || pawn.Map == null || props == null)
            {
                Stop();
                return true;
            }

            EnsureTimingLists(props);

            age++;

            TickTexts(pawn, props);
            TickSounds(pawn, props);
            TickFlecks(pawn, props);
            TickStun(pawn, props, sendMessages);
            TickAttachedMote(pawn, props);

            if (age >= GetCompletionTick(props))
            {
                Stop();
                return true;
            }

            return false;
        }

        public void Stop()
        {
            active = false;
            DestroyAttachedMote();
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref age, "age", 0);
            Scribe_Values.Look(ref stunTickTarget, "stunTickTarget", -1);
            Scribe_Values.Look(ref stunned, "stunned", false);
            Scribe_Values.Look(ref stunEndTick, "stunEndTick", -1);
            Scribe_Values.Look(ref stunMessageSent, "stunMessageSent", false);
            Scribe_Collections.Look(ref nextTextShowTicks, "nextTextShowTicks", LookMode.Value);
            Scribe_Collections.Look(ref nextSoundTicks, "nextSoundTicks", LookMode.Value);
            Scribe_Collections.Look(ref nextFleckTicks, "nextFleckTicks", LookMode.Value);
            Scribe_Values.Look(ref active, "active", false);

            if (Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (nextTextShowTicks == null) nextTextShowTicks = new List<int>();
                if (nextSoundTicks == null) nextSoundTicks = new List<int>();
                if (nextFleckTicks == null) nextFleckTicks = new List<int>();
            }
        }

        private void EnsureTimingLists(CompProperties_PerformanceEffect props)
        {
            if (nextTextShowTicks == null) nextTextShowTicks = new List<int>();
            if (nextSoundTicks == null) nextSoundTicks = new List<int>();
            if (nextFleckTicks == null) nextFleckTicks = new List<int>();

            if (props.texts != null)
            {
                while (nextTextShowTicks.Count < props.texts.Count)
                {
                    int index = nextTextShowTicks.Count;
                    CompProperties_PerformanceEffect.TextWithParams textParams = props.texts[index];
                    int startTick = textParams == null ? -1 : Mathf.Max(0, textParams.startTick);
                    nextTextShowTicks.Add(startTick >= age ? startTick : -1);
                }
            }

            if (props.sounds != null)
            {
                while (nextSoundTicks.Count < props.sounds.Count)
                {
                    int index = nextSoundTicks.Count;
                    CompProperties_PerformanceEffect.SoundWithParams soundParams = props.sounds[index];
                    nextSoundTicks.Add(soundParams == null ? -1 : Mathf.Max(age, soundParams.startTick));
                }
            }

            if (props.flecks != null)
            {
                while (nextFleckTicks.Count < props.flecks.Count)
                {
                    int index = nextFleckTicks.Count;
                    CompProperties_PerformanceEffect.FleckWithParams fleckParams = props.flecks[index];
                    nextFleckTicks.Add(fleckParams == null ? -1 : Mathf.Max(age, fleckParams.startTick));
                }
            }
        }

        private void TickTexts(Pawn pawn, CompProperties_PerformanceEffect props)
        {
            if (props.texts == null)
                return;

            for (int i = 0; i < props.texts.Count; i++)
            {
                if (i < nextTextShowTicks.Count && nextTextShowTicks[i] != -1 && age >= nextTextShowTicks[i])
                {
                    var textParams = props.texts[i];
                    if (textParams != null && !string.IsNullOrEmpty(textParams.text))
                    {
                        MoteMaker.ThrowText(pawn.DrawPos, pawn.Map, textParams.text, textParams.color);
                    }
                    nextTextShowTicks[i] = -1;
                }
            }
        }

        private void TickSounds(Pawn pawn, CompProperties_PerformanceEffect props)
        {
            if (props.sounds == null)
                return;

            for (int i = 0; i < props.sounds.Count; i++)
            {
                if (i >= nextSoundTicks.Count)
                    continue;

                var s = props.sounds[i];
                if (s == null || nextSoundTicks[i] == -1)
                    continue;

                int startTick = Mathf.Max(0, s.startTick);
                int endTick = Mathf.Max(startTick, s.endTick);
                if (age >= startTick && age <= endTick && age >= nextSoundTicks[i])
                {
                    SoundInfo info = SoundInfo.InMap(new TargetInfo(pawn.Position, pawn.Map));
                    s.sound?.PlayOneShot(info);
                    nextSoundTicks[i] = age + Mathf.Max(1, s.loopInterval);
                }
            }
        }

        private void TickFlecks(Pawn pawn, CompProperties_PerformanceEffect props)
        {
            if (props.flecks == null)
                return;

            for (int i = 0; i < props.flecks.Count; i++)
            {
                if (i >= nextFleckTicks.Count)
                    continue;

                var f = props.flecks[i];
                if (f == null || f.fleck == null || nextFleckTicks[i] == -1)
                    continue;

                int startTick = Mathf.Max(0, f.startTick);
                int endTick = Mathf.Max(startTick, f.endTick);
                if (age >= startTick && age <= endTick && age >= nextFleckTicks[i])
                {
                    IntVec3 offset = new IntVec3(Rand.RangeInclusive(-1, 1), 0, Rand.RangeInclusive(0, 1));
                    IntVec3 pos = pawn.Position + offset;
                    if (pos.InBounds(pawn.Map))
                    {
                        var data = FleckMaker.GetDataStatic(pos.ToVector3Shifted(), pawn.Map, f.fleck, 0.42f);
                        data.velocityAngle = 0f;
                        data.velocitySpeed = Rand.Range(0.2f, 0.5f);
                        pawn.Map.flecks.CreateFleck(data);
                    }
                    nextFleckTicks[i] = age + Mathf.Max(1, f.loopInterval);
                }
            }
        }

        private void TickStun(Pawn pawn, CompProperties_PerformanceEffect props, bool sendMessages)
        {
            if (!stunned && age >= stunTickTarget)
            {
                stunned = true;
                int stunDurationTicks = Mathf.Max(0, props.stunDurationTicks);
                stunEndTick = age + stunDurationTicks;
                if (sendMessages)
                {
                    Messages.Message("MooGirl.BrainwashStart".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.NegativeEvent);
                }
                if (stunDurationTicks > 0)
                {
                    pawn.stances?.stunner?.StunFor(stunDurationTicks, null, false, false, true);
                }
            }

            if (stunned && !stunMessageSent && age >= stunEndTick)
            {
                stunMessageSent = true;
                if (sendMessages)
                {
                    Messages.Message("MooGirl.BrainwashEnd".Translate(pawn.LabelShortCap), pawn, MessageTypeDefOf.PositiveEvent);
                }
            }
        }

        private void TickAttachedMote(Pawn pawn, CompProperties_PerformanceEffect props)
        {
            bool shouldShowMote = props.moteToShowOnHead != null
                && age >= props.moteShowStartTick
                && age <= props.moteShowEndTick;

            if (!shouldShowMote)
            {
                DestroyAttachedMote();
                return;
            }

            if (attachedMote == null || attachedMote.Destroyed)
            {
                Mote mote = ThingMaker.MakeThing(props.moteToShowOnHead, null) as Mote;
                if (mote == null)
                    return;

                mote.Attach(pawn, new Vector3(0f, 0f, 0.5f), false);
                GenSpawn.Spawn(mote, pawn.Position, pawn.Map, Rot4.North, WipeMode.Vanish, false, false);
                attachedMote = mote;
            }
            else
            {
                attachedMote.Maintain();
            }
        }

        private int GetCompletionTick(CompProperties_PerformanceEffect props)
        {
            int completionTick = Mathf.Max(1, props.triggerTicks + props.stunDelayTicks + props.stunDurationTicks);

            if (props.moteToShowOnHead != null)
            {
                completionTick = Mathf.Max(completionTick, props.moteShowEndTick);
            }

            if (props.sounds != null)
            {
                for (int i = 0; i < props.sounds.Count; i++)
                {
                    CompProperties_PerformanceEffect.SoundWithParams soundParams = props.sounds[i];
                    if (soundParams != null)
                    {
                        completionTick = Mathf.Max(completionTick, soundParams.endTick);
                    }
                }
            }

            if (props.flecks != null)
            {
                for (int i = 0; i < props.flecks.Count; i++)
                {
                    CompProperties_PerformanceEffect.FleckWithParams fleckParams = props.flecks[i];
                    if (fleckParams != null)
                    {
                        completionTick = Mathf.Max(completionTick, fleckParams.endTick);
                    }
                }
            }

            if (props.texts != null)
            {
                for (int i = 0; i < props.texts.Count; i++)
                {
                    CompProperties_PerformanceEffect.TextWithParams textParams = props.texts[i];
                    if (textParams != null)
                    {
                        completionTick = Mathf.Max(completionTick, textParams.startTick);
                    }
                }
            }

            return completionTick;
        }

        private void DestroyAttachedMote()
        {
            if (attachedMote != null && !attachedMote.Destroyed)
            {
                attachedMote.Destroy(DestroyMode.Vanish);
            }
            attachedMote = null;
        }
    }
}
