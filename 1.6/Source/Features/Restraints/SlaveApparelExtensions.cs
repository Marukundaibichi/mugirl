using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public static class SlaveApparelExtensions
    {
        public static bool IsSlaveApparel(this Apparel apparel)
        {
            return apparel is SlaveApparel;
        }

        public static bool SatisfiesKey(this Apparel apparel, Thing key)
        {
            return apparel is SlaveApparel && apparel.def is SlaveApparelDef def && (def.keytype == null || def.keytype == key.def);
        }

        public static bool IsAdvancedApparel(this Apparel apparel)
        {
            return apparel is AdvancedSlaveApparel || apparel is BrainWashSlaveApparel;
        }

        public static bool IsUnlockAdvancedApparel(this Apparel apparel)
        {
            if (apparel is AdvancedSlaveApparel advanced)
            {
                return advanced.IsCracked();
            }

            if (apparel is BrainWashSlaveApparel brainwashed)
            {
                return brainwashed.IsCracked();
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
                if (wornApparel[i] is AdvancedSlaveApparel || wornApparel[i] is BrainWashSlaveApparel)
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

        public static void StartUnlockJob(this CompUsable usable, Pawn pawn, LocalTargetInfo target, Apparel apparel)
        {
            if (pawn.CanReserveAndReach(usable.parent, PathEndMode.Touch, Danger.Some) &&
                ((target == null) || pawn.CanReserveAndReach(target, PathEndMode.Touch, Danger.Some)))
            {
                CompForbiddable forbiddable = usable.parent.GetComp<CompForbiddable>();
                if (forbiddable != null)
                {
                    forbiddable.Forbidden = false;
                }

                Job job = JobMaker.MakeJob(((CompProperties_Usable)usable.props).useJob, usable.parent, target, apparel);
                pawn.jobs.TryTakeOrderedJob(job);
            }
        }

        public static FloatMenuOption MakeUnlockOption(
            this CompUsable usable,
            string label,
            Pawn pawn,
            LocalTargetInfo target,
            WorkTypeDef requiredWork)
        {
            if ((target != null) && !pawn.CanReserve(target))
            {
                return DisabledOption(label, "MooGirl.Reserved".Translate().ToString());
            }

            if ((target != null) && !pawn.CanReach(target, PathEndMode.Touch, Danger.Some))
            {
                return DisabledOption(label, "MooGirl.NoPath".Translate().ToString());
            }

            if ((requiredWork != null) && pawn.WorkTagIsDisabled(requiredWork.workTags))
            {
                return DisabledOption(label, "MooGirl.CannotPrioritizeWorkTypeDisabled".Translate(requiredWork.gerundLabel).ToString());
            }

            return new FloatMenuOption(label, delegate
            {
                Pawn targetPawn = ResolveTargetPawn(pawn, target);
                if (targetPawn?.apparel == null)
                {
                    return;
                }

                List<FloatMenuOption> options = new List<FloatMenuOption>();
                List<Apparel> lockedApparel = targetPawn.apparel.LockedApparel;
                for (int i = 0; i < lockedApparel.Count; i++)
                {
                    Apparel item = lockedApparel[i];
                    if (item.SatisfiesKey(usable.parent))
                    {
                        options.Add(new FloatMenuOption(item.Label, () => usable.StartUnlockJob(pawn, target, item)));
                    }
                    else
                    {
                        options.Add(new FloatMenuOption(
                            "MooGirl.WrongKeyType".Translate(item.Label),
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
                    Find.WindowStack.Add(new FloatMenu(options));
                }
            },
            MenuOptionPriority.Default);
        }

        private static FloatMenuOption DisabledOption(string label, string reason)
        {
            return new FloatMenuOption(
                "MooGirl.FloatMenu.OptionWithReason".Translate(label, reason),
                null,
                MenuOptionPriority.DisabledOption);
        }

        private static Pawn ResolveTargetPawn(Pawn actor, LocalTargetInfo target)
        {
            if (target == null)
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
    }
}
