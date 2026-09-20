using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace Mugirl.Features.Lances
{
    public sealed class CompProperties_LanceCharge : CompProperties
    {
        public bool hasPointCharge = true;
        public bool hasLineCharge = true;
        public float minimumPointRange = 13f;
        public float minimumLineRange = 4f;
        public int pointCooldownTicks = 1800;
        public int lineCooldownTicks = 600;
        public int pointStunTicks = 300;
        public int lineStunTicks = 180;
        public int pointDurabilityCost = 20;
        public int lineDurabilityCost;
        public float maximumPointDamage = 220f;
        public bool steamTrail;
        public string pointIconPath;
        public string lineIconPath;

        public CompProperties_LanceCharge()
        {
            compClass = typeof(CompLanceCharge);
        }
    }

    public sealed class CompLanceCharge : ThingComp
    {
        private int pointCooldownUntilTick;
        private int lineCooldownUntilTick;

        public CompProperties_LanceCharge Props => (CompProperties_LanceCharge)props;

        private Pawn Wearer => (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref pointCooldownUntilTick, "pointCooldownUntilTick", 0);
            Scribe_Values.Look(ref lineCooldownUntilTick, "lineCooldownUntilTick", 0);
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            Pawn wearer = Wearer;
            if (wearer == null || !MugirlIdentity.IsMugirlPawn(wearer) || wearer.equipment?.Primary != parent)
            {
                yield break;
            }

            if (Props.hasPointCharge)
            {
                yield return CreateCommand(
                    point: true,
                    "Mugirl.Lance.PointCharge.Label".Translate(),
                    "Mugirl.Lance.PointCharge.Desc".Translate(),
                    Props.pointIconPath,
                    pointCooldownUntilTick,
                    Props.pointCooldownTicks);
            }

            if (Props.hasLineCharge)
            {
                yield return CreateCommand(
                    point: false,
                    "Mugirl.Lance.LineCharge.Label".Translate(),
                    "Mugirl.Lance.LineCharge.Desc".Translate(),
                    Props.lineIconPath,
                    lineCooldownUntilTick,
                    Props.lineCooldownTicks);
            }
        }

        private Command_ActionWithCooldown CreateCommand(bool point, string label, string description, string iconPath, int cooldownUntilTick, int cooldownTicks)
        {
            Pawn wearer = Wearer;
            int ticksLeft = Mathf.Max(0, cooldownUntilTick - CurrentTick);
            Command_ActionWithCooldown command = new Command_ActionWithCooldown
            {
                defaultLabel = label,
                defaultDesc = description,
                icon = iconPath.NullOrEmpty() ? BaseContent.BadTex : ContentFinder<Texture2D>.Get(iconPath, false) ?? BaseContent.BadTex,
                action = () => BeginTargeting(point),
                cooldownPercentGetter = () => CooldownPercent(point ? pointCooldownUntilTick : lineCooldownUntilTick, point ? Props.pointCooldownTicks : Props.lineCooldownTicks)
            };

            if (wearer?.Drafted != true)
            {
                command.Disable("Mugirl.Lance.MustBeDrafted".Translate());
            }
            else if (ticksLeft > 0)
            {
                command.Disable("Mugirl.Lance.Cooldown".Translate(ticksLeft.ToStringTicksToPeriod()));
            }

            return command;
        }

        private void BeginTargeting(bool point)
        {
            Pawn wearer = Wearer;
            if (wearer?.Spawned != true || wearer.Drafted != true)
            {
                return;
            }

            float minimumRange = point ? Props.minimumPointRange : Props.minimumLineRange;
            TargetingParameters parameters = new TargetingParameters
            {
                canTargetLocations = false,
                canTargetPawns = true,
                canTargetHumans = true,
                canTargetAnimals = true,
                canTargetMechs = true,
                canTargetBuildings = false,
                mapObjectTargetsMustBeAutoAttackable = false,
                validator = target => IsValidTarget(wearer, target.Thing as Pawn, minimumRange)
            };

            MugirlGameUtility.TryBeginTargeting(parameters, target => StartChargeJob(wearer, target.Thing as Pawn, point), wearer);
        }

        private void StartChargeJob(Pawn wearer, Pawn target, bool point)
        {
            float minimumRange = point ? Props.minimumPointRange : Props.minimumLineRange;
            if (!IsValidTarget(wearer, target, minimumRange))
            {
                Messages.Message("Mugirl.Lance.InvalidTarget".Translate(), wearer, MessageTypeDefOf.RejectInput, false);
                return;
            }

            JobDef jobDef = point ? Mugirl_DefOf.Job_MugirlLancePointCharge : Mugirl_DefOf.Job_MugirlLanceLineCharge;
            Job job = JobMaker.MakeJob(jobDef, target);
            job.playerForced = true;
            if (wearer.jobs.TryTakeOrderedJob(job, JobTag.Misc))
            {
                if (point)
                {
                    pointCooldownUntilTick = CurrentTick + Props.pointCooldownTicks;
                }
                else
                {
                    lineCooldownUntilTick = CurrentTick + Props.lineCooldownTicks;
                }
            }
        }

        private static bool IsValidTarget(Pawn wearer, Pawn target, float minimumRange)
        {
            return wearer?.Spawned == true
                && target?.Spawned == true
                && !target.Dead
                && target != wearer
                && target.Map == wearer.Map
                && wearer.HostileTo(target)
                && wearer.Position.DistanceTo(target.Position) >= minimumRange;
        }

        private static float CooldownPercent(int cooldownUntilTick, int cooldownTicks)
        {
            if (cooldownTicks <= 0)
            {
                return 1f;
            }

            return Mathf.Clamp01(1f - (float)Mathf.Max(0, cooldownUntilTick - CurrentTick) / cooldownTicks);
        }

        private static int CurrentTick
        {
            get
            {
                return MugirlTickUtility.TryGetCurrentGameTick(out int tick) ? tick : 0;
            }
        }
    }
}
