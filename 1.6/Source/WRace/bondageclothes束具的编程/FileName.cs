using HarmonyLib;
using RimWorld;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace MooGirl
{
    public class CompProperties_BrainWashingStar : HediffCompProperties
    {
        public CompProperties_BrainWashingStar()
        {
            compClass = typeof(HediffComp_BrainWashingStar);
        }

        public int triggerTicks = 60; // 音效触发延迟
        public int stunDelayTicks = 10;    // 音效播放后多久施加 Stun
        public int stunDurationTicks = 120; // Stun 持续时间

        public List<SoundDef> soundsToPlay = new List<SoundDef>();
    }

    public class HediffComp_BrainWashingStar : HediffComp
    {
        private int age = 0;
        private bool soundPlayed = false;

        public CompProperties_BrainWashingStar Props => (CompProperties_BrainWashingStar)props;

        public override void CompPostTick(ref float severityAdjustment)
        {
            base.CompPostTick(ref severityAdjustment);
            age++;

            if (!soundPlayed && age >= Props.triggerTicks)
            {
                soundPlayed = true;
                PlaySounds();

                // 延迟施加 stun
                stunTickTarget = age + Props.stunDelayTicks;
            }

            if (soundPlayed && stunTickTarget > 0 && age >= stunTickTarget)
            {
                ApplyStun();
                stunTickTarget = -1; // 防止重复
            }
        }

        private int stunTickTarget = -1;

        private void PlaySounds()
        {
            if (Pawn != null && Pawn.Spawned && Props.soundsToPlay != null)
            {
                foreach (var sound in Props.soundsToPlay)
                {
                    sound?.PlayOneShot(new TargetInfo(Pawn.Position, Pawn.Map));
                }
            }
        }

        private void ApplyStun()
        {
            if (Pawn != null && Pawn.Spawned && Pawn.stances?.stunner != null)
            {
                Pawn.stances.stunner.StunFor(Props.stunDurationTicks, null);
            }
        }
    }
}
