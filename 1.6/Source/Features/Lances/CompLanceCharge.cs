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
        public float minimumPointRange;
        public float minimumLineRange = 4f;
        public float maximumRange = 19.9f;
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

    public sealed class CompLanceCharge : ThingComp, ITargetingSource
    {
        private int pointCooldownUntilTick;
        private int lineCooldownUntilTick;
        private bool targetingPointCharge;
        private Pawn targetingWearer;
        private Texture2D targetingIcon;

        public CompProperties_LanceCharge Props => (CompProperties_LanceCharge)props;

        private Pawn Wearer => (parent.ParentHolder as Pawn_EquipmentTracker)?.pawn;

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_Values.Look(ref pointCooldownUntilTick, "pointCooldownUntilTick", 0);
            Scribe_Values.Look(ref lineCooldownUntilTick, "lineCooldownUntilTick", 0);
        }

        // 武器装备后由装备栏补丁转发；原版装备栏不会调用武器的 CompGetGizmosExtra。
        public IEnumerable<Gizmo> GetEquippedGizmos()
        {
            Pawn wearer = Wearer;
            if (wearer == null || !wearer.IsColonistPlayerControlled
                || !MugirlIdentity.IsMugirlPawn(wearer) || wearer.equipment?.Primary != parent)
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

            targetingPointCharge = point;
            targetingWearer = wearer;
            string iconPath = point ? Props.pointIconPath : Props.lineIconPath;
            targetingIcon = iconPath.NullOrEmpty()
                ? BaseContent.BadTex
                : ContentFinder<Texture2D>.Get(iconPath, false) ?? BaseContent.BadTex;

            MugirlGameUtility.TryBeginTargeting(
                this,
                actionWhenFinished: null,
                allowNonSelectedTargetingSource: false,
                requiresAvailableVerb: false);
        }

        private void StartChargeJob(Pawn wearer, LocalTargetInfo target, bool point)
        {
            float minimumRange = point ? Props.minimumPointRange : Props.minimumLineRange;
            if (!IsValidTarget(wearer, target, point, minimumRange, Props.maximumRange))
            {
                RejectInvalidTarget(wearer, point, minimumRange);
                return;
            }

            JobDef jobDef = point ? Mugirl_DefOf.Job_MugirlLancePointCharge : Mugirl_DefOf.Job_MugirlLanceLineCharge;
            // 沿途冲锋锁定点击时的格子，不追逐会移动的 Pawn；定点冲锋仍锁定敌对 Pawn。
            Job job = point
                ? JobMaker.MakeJob(jobDef, target, new LocalTargetInfo(target.Cell))
                : JobMaker.MakeJob(jobDef, new LocalTargetInfo(target.Cell));
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

        private bool IsValidTarget(Pawn wearer, LocalTargetInfo target, bool point, float minimumRange, float maximumRange)
        {
            if (wearer?.Spawned != true || wearer.equipment?.Primary != parent || !target.IsValid
                || !target.Cell.IsValid || !target.Cell.InBounds(wearer.Map))
            {
                return false;
            }

            float distance = wearer.Position.DistanceTo(target.Cell);
            if (distance < minimumRange || distance > maximumRange)
            {
                return false;
            }

            if (target.Thing == null)
            {
                return !point && LanceChargeDestinationUtility.TryFindLandingCell(wearer, target.Cell, false, out _);
            }

            Pawn targetPawn = target.Thing as Pawn;
            bool validPawn = targetPawn?.Spawned == true
                && !targetPawn.Dead
                && targetPawn != wearer
                && targetPawn.Map == wearer.Map
                && wearer.HostileTo(targetPawn);
            return validPawn && LanceChargeDestinationUtility.TryFindLandingCell(wearer, targetPawn.Position, point, out _);
        }

        private void RejectInvalidTarget(Pawn wearer, bool point, float minimumRange)
        {
            string key = point ? "Mugirl.Lance.PointCharge.InvalidTarget" : "Mugirl.Lance.LineCharge.InvalidTarget";
            Messages.Message(
                key.Translate(minimumRange.ToString("0.#"), Props.maximumRange.ToString("0.#")),
                wearer,
                MessageTypeDefOf.RejectInput,
                false);
        }

        public bool CasterIsPawn => true;
        public bool IsMeleeAttack => false;
        public bool Targetable => true;
        public bool MultiSelect => false;
        public bool HidePawnTooltips => false;
        public Thing Caster => targetingWearer ?? Wearer;
        public Pawn CasterPawn => targetingWearer ?? Wearer;
        public Verb GetVerb => null;
        public Texture2D UIIcon => targetingIcon ?? BaseContent.BadTex;
        public ITargetingSource DestinationSelector => null;

        public TargetingParameters targetParams => new TargetingParameters
        {
            canTargetLocations = !targetingPointCharge,
            canTargetPawns = true,
            canTargetHumans = true,
            canTargetAnimals = true,
            canTargetMechs = true,
            canTargetSubhumans = true,
            canTargetEntities = true,
            canTargetBloodfeeders = true,
            canTargetBuildings = false,
            mapObjectTargetsMustBeAutoAttackable = false,
            validator = target => CanHitTarget(target.HasThing
                ? new LocalTargetInfo(target.Thing)
                : new LocalTargetInfo(target.Cell))
        };

        public bool CanHitTarget(LocalTargetInfo target)
        {
            float minimumRange = targetingPointCharge ? Props.minimumPointRange : Props.minimumLineRange;
            return IsValidTarget(CasterPawn, target, targetingPointCharge, minimumRange, Props.maximumRange);
        }

        public bool ValidateTarget(LocalTargetInfo target, bool showMessages = true)
        {
            if (CanHitTarget(target))
            {
                return true;
            }

            if (showMessages)
            {
                float minimumRange = targetingPointCharge ? Props.minimumPointRange : Props.minimumLineRange;
                RejectInvalidTarget(CasterPawn, targetingPointCharge, minimumRange);
            }
            return false;
        }

        public void DrawHighlight(LocalTargetInfo target)
        {
            Pawn wearer = CasterPawn;
            if (wearer?.Spawned == true)
            {
                GenDraw.DrawRadiusRing(wearer.Position, Props.maximumRange);
                float minimumRange = targetingPointCharge ? Props.minimumPointRange : Props.minimumLineRange;
                if (minimumRange > 0.1f && minimumRange < 90f)
                {
                    GenDraw.DrawRadiusRing(wearer.Position, minimumRange);
                }
            }
            if (CanHitTarget(target))
            {
                GenDraw.DrawTargetHighlight(target);
            }
        }

        public void OrderForceTarget(LocalTargetInfo target)
        {
            Pawn wearer = CasterPawn;
            if (ValidateTarget(target))
            {
                StartChargeJob(wearer, target, targetingPointCharge);
            }
        }

        public void OnGUI(LocalTargetInfo target)
        {
            GenUI.DrawMouseAttachment(CanHitTarget(target) ? UIIcon : TexCommand.CannotShoot);
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
