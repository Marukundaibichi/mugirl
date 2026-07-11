using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace Mugirl
{
    public static class SlaveApparelExtensions
    {
        public static bool IsSlaveApparel(this Apparel apparel)
        {
            return apparel is SlaveApparel;
        }

        public static bool IsLockedSlaveApparel(this Apparel apparel)
        {
            return apparel is SlaveApparel slaveApparel
                && slaveApparel.isLocked
                && (!(slaveApparel is AdvancedSlaveApparel advanced) || !advanced.IsCracked());
        }

        public static bool IsWornLockedSlaveApparel(this Pawn pawn, Apparel apparel)
        {
            return pawn?.apparel != null
                && apparel != null
                && pawn.apparel.WornApparel.Contains(apparel)
                && apparel.IsLockedSlaveApparel();
        }

        public static bool SatisfiesKey(this Apparel apparel, Thing key)
        {
            return key != null && apparel is SlaveApparel && apparel.def is SlaveApparelDef def && (def.keytype == null || def.keytype == key.def);
        }

        public static bool IsAdvancedApparel(this Apparel apparel)
        {
            return apparel is AdvancedSlaveApparel;
        }

        public static bool IsUnlockAdvancedApparel(this Apparel apparel)
        {
            if (apparel is AdvancedSlaveApparel advanced)
            {
                return advanced.IsCracked();
            }

            return false;
        }

        public static bool IsWearingSlaveApparel(this Pawn pawn)
        {
            if (pawn?.apparel == null)
            {
                return false;
            }

            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                if (wornApparel[i] is SlaveApparel)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsWearingAdvancedApparel(this Pawn pawn)
        {
            if (pawn?.apparel == null)
            {
                return false;
            }

            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                if (wornApparel[i] is AdvancedSlaveApparel)
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsWearingUncrackedBrainwashApparel(this Pawn pawn)
        {
            if (pawn?.apparel == null)
            {
                return false;
            }

            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                if (wornApparel[i] is BrainWashSlaveApparel slaveApparel && !slaveApparel.IsCracked())
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsWearingCrackedBrainwashApparel(this Pawn pawn)
        {
            if (pawn?.apparel == null)
            {
                return false;
            }

            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                if (wornApparel[i] is BrainWashSlaveApparel slaveApparel && slaveApparel.IsCracked())
                {
                    return true;
                }
            }

            return false;
        }

        public static bool IsHandsBlocked(this Pawn pawn)
        {
            if (pawn?.apparel == null)
            {
                return false;
            }

            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                if (wornApparel[i] is SlaveApparel slaveApparel && slaveApparel.SlaveDef.blocks_hands)
                {
                    return true;
                }
            }

            return false;
        }

        public static void EnsureWornSlaveApparelLocks(this Pawn pawn)
        {
            if (pawn?.apparel?.WornApparel == null)
            {
                return;
            }

            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                Apparel apparel = wornApparel[i];
                if (apparel.IsLockedSlaveApparel() && !pawn.apparel.IsLocked(apparel))
                {
                    pawn.apparel.Lock(apparel);
                }
            }
        }

        public static void StartUnlockJob(this CompUsable usable, Pawn pawn, LocalTargetInfo target, Apparel apparel)
        {
            if (usable == null || pawn == null || apparel == null)
            {
                return;
            }

            LocalTargetInfo jobTarget = ResolveJobTarget(pawn, target);
            Pawn targetPawn = ResolveTargetPawn(pawn, jobTarget);
            if (targetPawn?.apparel == null ||
                !targetPawn.IsWornLockedSlaveApparel(apparel) ||
                !apparel.SatisfiesKey(usable.parent))
            {
                return;
            }

            if (!pawn.CanReserveAndReach(usable.parent, PathEndMode.Touch, Danger.Some))
            {
                return;
            }

            if (targetPawn != pawn && !pawn.CanReserveAndReach(jobTarget, PathEndMode.Touch, Danger.Some))
            {
                return;
            }

            CompForbiddable forbiddable = usable.parent.GetComp<CompForbiddable>();
            if (forbiddable != null)
            {
                forbiddable.Forbidden = false;
            }

            CompProperties_Usable usableProps = usable.props as CompProperties_Usable;
            if (usableProps?.useJob == null)
            {
                return;
            }

            Job job = JobMaker.MakeJob(usableProps.useJob, usable.parent, jobTarget, apparel);
            pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc);
        }

        public static FloatMenuOption MakeUnlockOption(
            this CompUsable usable,
            string label,
            Pawn pawn,
            LocalTargetInfo target,
            WorkTypeDef requiredWork)
        {
            LocalTargetInfo jobTarget = ResolveJobTarget(pawn, target);
            Pawn targetPawn = ResolveTargetPawn(pawn, jobTarget);
            if (targetPawn?.apparel == null)
            {
                return DisabledOption(label, "Mugirl.InvalidTarget".Translate().ToString());
            }

            if (!HasLockedApparel(targetPawn))
            {
                return DisabledOption(label, "Mugirl.NotWearingLockedApparel".Translate().ToString());
            }

            if (targetPawn != pawn && !pawn.CanReserve(jobTarget))
            {
                return DisabledOption(label, "Mugirl.Reserved".Translate().ToString());
            }

            if (targetPawn != pawn && !pawn.CanReach(jobTarget, PathEndMode.Touch, Danger.Some))
            {
                return DisabledOption(label, "Mugirl.NoPath".Translate().ToString());
            }

            if ((requiredWork != null) && pawn.WorkTagIsDisabled(requiredWork.workTags))
            {
                return DisabledOption(label, "Mugirl.CannotPrioritizeWorkTypeDisabled".Translate(requiredWork.gerundLabel).ToString());
            }

            return new FloatMenuOption(label, delegate
            {
                Pawn currentTargetPawn = ResolveTargetPawn(pawn, jobTarget);
                if (currentTargetPawn?.apparel == null)
                {
                    return;
                }

                List<FloatMenuOption> options = new List<FloatMenuOption>();
                List<Apparel> wornApparel = currentTargetPawn.apparel.WornApparel;
                for (int i = 0; i < wornApparel.Count; i++)
                {
                    Apparel item = wornApparel[i];
                    if (!currentTargetPawn.IsWornLockedSlaveApparel(item))
                    {
                        continue;
                    }

                    if (item.SatisfiesKey(usable.parent))
                    {
                        options.Add(new FloatMenuOption(item.Label, () => usable.StartUnlockJob(pawn, jobTarget, item)));
                    }
                    else
                    {
                        options.Add(new FloatMenuOption(
                            "Mugirl.WrongKeyType".Translate(item.Label),
                            null,
                            MenuOptionPriority.Default,
                            null,
                            null,
                            0f,
                            null,
                            null,
                            true));
                    }
                }

                if (options.Count > 0)
                {
                    MugirlGameUtility.TryAddWindow(new FloatMenu(options));
                }
                else
                {
                    Messages.Message("Mugirl.NotWearingLockedApparel".Translate(), MessageTypeDefOf.RejectInput, historical: false);
                }
            },
            MenuOptionPriority.Default);
        }

        private static FloatMenuOption DisabledOption(string label, string reason)
        {
            return new FloatMenuOption(
                "Mugirl.FloatMenu.OptionWithReason".Translate(label, reason),
                null,
                MenuOptionPriority.DisabledOption);
        }

        private static Pawn ResolveTargetPawn(Pawn actor, LocalTargetInfo target)
        {
            if (!target.IsValid)
            {
                return actor;
            }

            if (target.Thing is Pawn pawn)
            {
                return pawn;
            }

            if (target.Thing is Corpse corpse)
            {
                return corpse.InnerPawn;
            }

            return null;
        }

        private static LocalTargetInfo ResolveJobTarget(Pawn actor, LocalTargetInfo target)
        {
            if (target.IsValid)
            {
                return target;
            }

            return actor;
        }

        private static bool HasLockedApparel(Pawn pawn)
        {
            if (pawn?.apparel == null)
            {
                return false;
            }

            List<Apparel> wornApparel = pawn.apparel.WornApparel;
            for (int i = 0; i < wornApparel.Count; i++)
            {
                if (pawn.IsWornLockedSlaveApparel(wornApparel[i]))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
