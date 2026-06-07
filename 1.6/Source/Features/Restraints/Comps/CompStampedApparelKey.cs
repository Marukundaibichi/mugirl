using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;

namespace MooGirl
{
    public class CompStampedApparelKey : CompUsable
    {
        protected string MakeLabel(Pawn pawn, Pawn other)
        {
            string targetLabel = other == null ? "MooGirl.SelfLabel".Translate().ToString() : PawnSlaveStatusUtility.DisplayName(other);
            return "MooGirl.FloatMenu.ActionOnTarget".Translate(FloatMenuOptionLabel(pawn), targetLabel).ToString();
        }

        public override IEnumerable<FloatMenuOption> CompFloatMenuOptions(Pawn pawn)
        {
            if (!pawn.CanReserve(parent))
            {
                yield return new FloatMenuOption(
                    "MooGirl.FloatMenu.OptionWithReason".Translate(FloatMenuOptionLabel(pawn), "MooGirl.Reserved".Translate()),
                    null,
                    MenuOptionPriority.DisabledOption
                );
            }
            else if (pawn.CanReach(parent, PathEndMode.Touch, Danger.Some))
            {
                // 钥匙可对自己、自由殖民者、囚犯和尸体中的 Pawn 生效；目标范围沿用旧实现。
                if (pawn.IsWearingSlaveApparel())
                {
                    if (!pawn.IsHandsBlocked())
                    {
                        yield return this.MakeUnlockOption(MakeLabel(pawn, pawn), pawn, pawn, null);
                    }
                    else
                    {
                        yield return new FloatMenuOption(
                            "MooGirl.FloatMenu.OptionWithReason".Translate(MakeLabel(pawn, pawn), "MooGirl.HandsBlocked".Translate()),
                            null,
                            MenuOptionPriority.DisabledOption);
                    }
                }

                if (MooGirlGameUtility.IsCurrentMap(pawn.Map))
                {
                    foreach (var other in pawn.Map.mapPawns.FreeColonists)
                    {
                        if ((other != pawn) && other.IsWearingSlaveApparel())
                        {
                            yield return this.MakeUnlockOption(MakeLabel(pawn, other), pawn, other, null);
                        }
                    }

                    foreach (var prisoner in pawn.Map.mapPawns.PrisonersOfColony)
                    {
                        if (prisoner.IsWearingSlaveApparel())
                        {
                            yield return this.MakeUnlockOption(MakeLabel(pawn, prisoner), pawn, prisoner, WorkTypeDefOf.Warden);
                        }
                    }

                    foreach (var q in pawn.Map.listerThings.ThingsInGroup(ThingRequestGroup.Corpse))
                    {
                        var corpse = q as Corpse;
                        if (corpse?.InnerPawn?.IsWearingSlaveApparel() == true)
                        {
                            yield return this.MakeUnlockOption(MakeLabel(pawn, corpse.InnerPawn), pawn, corpse, null);
                        }
                    }
                }
            }
        }

    }
}
